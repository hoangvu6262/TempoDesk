using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.Workspaces;

internal sealed class WorkspaceMembershipService(
    TempoDeskIdentityDbContext context,
    IWorkspaceAuthorizationService authorization,
    ILookupNormalizer normalizer,
    IWorkspaceInvitationSender invitationSender,
    TimeProvider timeProvider) : IWorkspaceMembershipService
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    public async Task<WorkspaceMembershipResult> ListMembersAsync(
        Guid actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var access = await authorization.AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.ViewMembers,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure(access);

        var members = await context.WorkspaceMembers
            .AsNoTracking()
            .Where(member => member.WorkspaceId == workspaceId &&
                member.RemovedAt == null)
            .OrderBy(member => member.CreatedAt)
            .ThenBy(member => member.Id)
            .Select(member => new WorkspaceMemberSummary(
                member.Id,
                member.User.Email!,
                member.Role,
                member.CreatedAt))
            .ToListAsync(cancellationToken);

        return WorkspaceMembershipResult.WithMembers(members);
    }

    public async Task<WorkspaceMembershipResult> ListInvitationsAsync(
        Guid actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var access = await authorization.AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.ViewMembers,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure(access);

        var now = timeProvider.GetUtcNow();
        var invitations = await context.WorkspaceInvitations
            .AsNoTracking()
            .Where(invitation => invitation.WorkspaceId == workspaceId &&
                invitation.AcceptedAt == null &&
                invitation.CancelledAt == null &&
                invitation.ExpiresAt > now)
            .OrderBy(invitation => invitation.CreatedAt)
            .ThenBy(invitation => invitation.Id)
            .Select(invitation => new WorkspaceInvitationSummary(
                invitation.Id,
                invitation.Email,
                invitation.Role,
                invitation.CreatedAt,
                invitation.ExpiresAt))
            .ToListAsync(cancellationToken);

        return WorkspaceMembershipResult.WithInvitations(invitations);
    }

    public async Task<WorkspaceMembershipResult> InviteAsync(
        Guid actorUserId,
        Guid workspaceId,
        string? email,
        string? role,
        CancellationToken cancellationToken = default)
    {
        var trimmedEmail = email?.Trim();
        if (string.IsNullOrEmpty(trimmedEmail) || trimmedEmail.Length > 256 ||
            !MailAddress.TryCreate(trimmedEmail, out var parsed) ||
            !string.Equals(parsed.Address, trimmedEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceMembershipResult.Invalid(
                "email", "A valid email address is required.");
        }

        var requestedRole = role?.Trim().ToLowerInvariant();
        var action = requestedRole is null
            ? null
            : WorkspaceMembershipPolicy.InvitationAction(requestedRole);
        if (action is null)
        {
            return WorkspaceMembershipResult.Invalid(
                "role", "Role must be admin, member, or viewer.");
        }

        var normalizedEmail = normalizer.NormalizeEmail(trimmedEmail);
        if (string.IsNullOrEmpty(normalizedEmail) ||
            normalizedEmail.Length > 256)
        {
            return WorkspaceMembershipResult.Invalid(
                "email", "A valid email address is required.");
        }

        var now = timeProvider.GetUtcNow();
        var rawToken = WorkspaceInvitationToken.Create();
        WorkspaceInvitationSummary summary;
        string workspaceName;

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            var workspace = await LockWorkspaceAsync(
                workspaceId, cancellationToken);
            if (workspace is null)
                return WorkspaceMembershipResult.Missing;

            var access = await authorization.AuthorizeAsync(
                actorUserId, workspaceId, action.Value, cancellationToken);
            if (access.Status != WorkspaceAuthorizationStatus.Allowed)
                return AuthorizationFailure(access);

            if (workspace.Type != WorkspaceTypeValues.Team)
                return WorkspaceMembershipResult.Conflicted;

            var existing = await context.WorkspaceInvitations
                .SingleOrDefaultAsync(invitation =>
                    invitation.WorkspaceId == workspaceId &&
                    invitation.NormalizedEmail == normalizedEmail &&
                    invitation.AcceptedAt == null &&
                    invitation.CancelledAt == null,
                    cancellationToken);
            if (existing is not null)
            {
                if (existing.ExpiresAt > now)
                    return WorkspaceMembershipResult.Conflicted;

                existing.CancelledAt = now;
            }

            var alreadyMember = await context.WorkspaceMembers
                .AnyAsync(member => member.WorkspaceId == workspaceId &&
                    member.RemovedAt == null &&
                    member.User.NormalizedEmail == normalizedEmail,
                    cancellationToken);
            if (alreadyMember)
                return WorkspaceMembershipResult.Conflicted;

            var invitation = new WorkspaceInvitation
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                InvitedByUserId = actorUserId,
                Email = trimmedEmail,
                NormalizedEmail = normalizedEmail,
                Role = requestedRole!,
                TokenHash = HashToken(rawToken),
                CreatedAt = now,
                ExpiresAt = now.Add(InvitationLifetime)
            };
            context.WorkspaceInvitations.Add(invitation);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            workspaceName = workspace.Name;
            summary = ToSummary(invitation);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_workspace_invitations_active_email" or
                    "ux_workspace_invitations_token_hash"
            })
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return WorkspaceMembershipResult.Conflicted;
        }

        try
        {
            await invitationSender.SendAsync(
                new WorkspaceInvitationEmail(
                    trimmedEmail,
                    workspaceName,
                    rawToken,
                    summary.ExpiresAt),
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Delivery failure does not expose or invalidate the invitation.
        }

        return WorkspaceMembershipResult.WithInvitation(summary);
    }

    public async Task<WorkspaceMembershipResult> CancelInvitationAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var workspace = await LockWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace is null)
            return WorkspaceMembershipResult.Missing;

        var invitation = await context.WorkspaceInvitations
            .FromSqlInterpolated($"""
                SELECT * FROM public.workspace_invitations
                WHERE id = {invitationId} AND workspace_id = {workspaceId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (invitation is null || invitation.AcceptedAt is not null ||
            invitation.CancelledAt is not null)
        {
            return WorkspaceMembershipResult.Missing;
        }

        var action = WorkspaceMembershipPolicy.InvitationAction(
            invitation.Role);
        if (action is null)
            return WorkspaceMembershipResult.Denied;

        var access = await authorization.AuthorizeAsync(
            actorUserId, workspaceId, action.Value, cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure(access);

        if (workspace.Type != WorkspaceTypeValues.Team)
            return WorkspaceMembershipResult.Conflicted;

        invitation.CancelledAt = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkspaceMembershipResult.Succeeded;
    }

    public async Task<WorkspaceMembershipResult> AcceptAsync(
        Guid userId,
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (!WorkspaceInvitationToken.TryHash(token, out var tokenHash))
            return WorkspaceMembershipResult.InvalidToken;

        var now = timeProvider.GetUtcNow();
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var invitation = await context.WorkspaceInvitations
            .FromSqlInterpolated($"""
                SELECT * FROM public.workspace_invitations
                WHERE token_hash = {tokenHash}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (invitation is null || invitation.AcceptedAt is not null ||
            invitation.CancelledAt is not null)
        {
            return WorkspaceMembershipResult.InvalidToken;
        }

        if (invitation.ExpiresAt <= now)
        {
            invitation.CancelledAt = now;
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return WorkspaceMembershipResult.InvalidToken;
        }

        var workspaceType = await context.Workspaces
            .Where(workspace => workspace.Id == invitation.WorkspaceId)
            .Select(workspace => workspace.Type)
            .SingleOrDefaultAsync(cancellationToken);
        if (workspaceType != WorkspaceTypeValues.Team)
            return WorkspaceMembershipResult.Conflicted;

        var user = await context.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new
            {
                candidate.Email,
                candidate.NormalizedEmail
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (user?.NormalizedEmail is null ||
            !string.Equals(user.NormalizedEmail,
                invitation.NormalizedEmail, StringComparison.Ordinal))
        {
            return WorkspaceMembershipResult.InvalidToken;
        }

        var alreadyMember = await context.WorkspaceMembers.AnyAsync(
            member => member.WorkspaceId == invitation.WorkspaceId &&
                member.UserId == userId && member.RemovedAt == null,
            cancellationToken);
        if (alreadyMember)
            return WorkspaceMembershipResult.InvalidToken;

        var membership = new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = invitation.WorkspaceId,
            UserId = userId,
            Role = invitation.Role,
            CreatedAt = now
        };
        context.WorkspaceMembers.Add(membership);
        invitation.AcceptedAt = now;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_workspace_members_active_user"
            })
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            return WorkspaceMembershipResult.InvalidToken;
        }

        return WorkspaceMembershipResult.WithMember(new WorkspaceMemberSummary(
            membership.Id,
            user.Email!,
            membership.Role,
            membership.CreatedAt));
    }

    public async Task<WorkspaceMembershipResult> ChangeRoleAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid memberId,
        string? role,
        CancellationToken cancellationToken = default)
    {
        var requestedRole = role?.Trim().ToLowerInvariant();
        if (!WorkspaceMembershipPolicy.IsManagedRole(requestedRole))
        {
            return WorkspaceMembershipResult.Invalid(
                "role", "Role must be admin, member, or viewer.");
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var workspace = await LockWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace is null)
            return WorkspaceMembershipResult.Missing;

        if (workspace.Type != WorkspaceTypeValues.Team)
        {
            var view = await authorization.AuthorizeAsync(
                actorUserId, workspaceId, WorkspaceAction.ViewWorkspace,
                cancellationToken);
            return view.Status == WorkspaceAuthorizationStatus.Allowed
                ? WorkspaceMembershipResult.Conflicted
                : AuthorizationFailure(view);
        }

        var membership = await context.WorkspaceMembers
            .Include(member => member.User)
            .SingleOrDefaultAsync(member => member.Id == memberId &&
                member.WorkspaceId == workspaceId &&
                member.RemovedAt == null,
                cancellationToken);
        if (membership is null)
            return WorkspaceMembershipResult.Missing;

        var action = WorkspaceMembershipPolicy.RoleChangeAction(
            membership.Role, requestedRole!);
        if (action is null)
            return WorkspaceMembershipResult.Denied;

        var access = await authorization.AuthorizeAsync(
            actorUserId, workspaceId, action.Value, cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure(access);

        membership.Role = requestedRole!;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkspaceMembershipResult.WithMember(ToSummary(membership));
    }

    public async Task<WorkspaceMembershipResult> RemoveMemberAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var workspace = await LockWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace is null)
            return WorkspaceMembershipResult.Missing;

        if (workspace.Type != WorkspaceTypeValues.Team)
        {
            var view = await authorization.AuthorizeAsync(
                actorUserId, workspaceId, WorkspaceAction.ViewWorkspace,
                cancellationToken);
            return view.Status == WorkspaceAuthorizationStatus.Allowed
                ? WorkspaceMembershipResult.Conflicted
                : AuthorizationFailure(view);
        }

        var membership = await context.WorkspaceMembers.SingleOrDefaultAsync(
            member => member.Id == memberId &&
                member.WorkspaceId == workspaceId &&
                member.RemovedAt == null,
            cancellationToken);
        if (membership is null)
            return WorkspaceMembershipResult.Missing;

        var action = WorkspaceMembershipPolicy.RemovalAction(membership.Role);
        if (action is null)
            return WorkspaceMembershipResult.Denied;

        var access = await authorization.AuthorizeAsync(
            actorUserId, workspaceId, action.Value, cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure(access);

        membership.RemovedAt = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkspaceMembershipResult.Succeeded;
    }

    public async Task<WorkspaceMembershipResult> LeaveAsync(
        Guid actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var workspace = await LockWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace is null)
            return WorkspaceMembershipResult.Missing;

        if (workspace.Type != WorkspaceTypeValues.Team)
        {
            var view = await authorization.AuthorizeAsync(
                actorUserId, workspaceId, WorkspaceAction.ViewWorkspace,
                cancellationToken);
            return view.Status == WorkspaceAuthorizationStatus.Allowed
                ? WorkspaceMembershipResult.Conflicted
                : AuthorizationFailure(view);
        }

        var access = await authorization.AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.LeaveWorkspace,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure(access);

        var membership = await context.WorkspaceMembers.SingleOrDefaultAsync(
            member => member.WorkspaceId == workspaceId &&
                member.UserId == actorUserId && member.RemovedAt == null,
            cancellationToken);
        if (membership is null)
            return WorkspaceMembershipResult.Missing;

        membership.RemovedAt = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkspaceMembershipResult.Succeeded;
    }

    public async Task<WorkspaceMembershipResult> TransferOwnershipAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid targetMemberId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        var workspace = await LockWorkspaceAsync(workspaceId, cancellationToken);
        if (workspace is null)
            return WorkspaceMembershipResult.Missing;

        if (workspace.Type != WorkspaceTypeValues.Team)
        {
            var view = await authorization.AuthorizeAsync(
                actorUserId, workspaceId, WorkspaceAction.ViewWorkspace,
                cancellationToken);
            return view.Status == WorkspaceAuthorizationStatus.Allowed
                ? WorkspaceMembershipResult.Conflicted
                : AuthorizationFailure(view);
        }

        var access = await authorization.AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.TransferOwnership,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure(access);

        var target = await context.WorkspaceMembers.SingleOrDefaultAsync(
            member => member.Id == targetMemberId &&
                member.WorkspaceId == workspaceId &&
                member.RemovedAt == null,
            cancellationToken);
        if (target is null)
            return WorkspaceMembershipResult.Missing;
        if (target.UserId == workspace.OwnerUserId)
            return WorkspaceMembershipResult.Conflicted;

        var currentOwner = await context.WorkspaceMembers.SingleAsync(
            member => member.WorkspaceId == workspaceId &&
                member.UserId == workspace.OwnerUserId &&
                member.Role == WorkspaceRoles.Owner &&
                member.RemovedAt == null,
            cancellationToken);

        currentOwner.Role = WorkspaceRoles.Admin;
        await context.SaveChangesAsync(cancellationToken);

        target.Role = WorkspaceRoles.Owner;
        workspace.OwnerUserId = target.UserId;
        workspace.UpdatedAt = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkspaceMembershipResult.Succeeded;
    }

    private Task<Workspace?> LockWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken) => context.Workspaces
        .FromSqlInterpolated($"""
            SELECT * FROM public.workspaces
            WHERE id = {workspaceId}
            FOR UPDATE
            """)
        .SingleOrDefaultAsync(cancellationToken);

    private static string HashToken(string token)
    {
        if (!WorkspaceInvitationToken.TryHash(token, out var hash))
            throw new InvalidOperationException("Generated token was invalid.");

        return hash;
    }

    private static WorkspaceInvitationSummary ToSummary(
        WorkspaceInvitation invitation) => new(
            invitation.Id,
            invitation.Email,
            invitation.Role,
            invitation.CreatedAt,
            invitation.ExpiresAt);

    private static WorkspaceMemberSummary ToSummary(
        WorkspaceMember member) => new(
            member.Id,
            member.User.Email!,
            member.Role,
            member.CreatedAt);

    private static WorkspaceMembershipResult AuthorizationFailure(
        WorkspaceAuthorizationResult authorization) =>
        authorization.Status switch
        {
            WorkspaceAuthorizationStatus.NotFound =>
                WorkspaceMembershipResult.Missing,
            WorkspaceAuthorizationStatus.Forbidden =>
                WorkspaceMembershipResult.Denied,
            _ => throw new InvalidOperationException(
                "Allowed authorization is not a failure.")
        };
}
