using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.Workspaces;

internal sealed class WorkspaceService(
    TempoDeskIdentityDbContext context,
    TimeProvider timeProvider) : IWorkspaceService
{
    public async Task<IReadOnlyList<WorkspaceSummary>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.WorkspaceMembers
            .AsNoTracking()
            .Where(member => member.UserId == userId &&
                member.RemovedAt == null)
            .OrderBy(member => member.Workspace.Type ==
                WorkspaceTypeValues.Personal ? 0 : 1)
            .ThenBy(member => member.Workspace.Name)
            .ThenBy(member => member.WorkspaceId)
            .Select(member => new WorkspaceSummary(
                member.WorkspaceId,
                member.Workspace.Name,
                member.Workspace.Type,
                member.Role))
            .ToListAsync(cancellationToken);

    public async Task<WorkspaceResult> CreateTeamAsync(
        Guid userId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        var normalized = WorkspaceName.Normalize(name, out var error);
        if (normalized is null)
            return WorkspaceResult.Invalid(error!);

        var now = timeProvider.GetUtcNow();
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = normalized,
            Type = WorkspaceTypeValues.Team,
            OwnerUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        var membership = new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceRoleValues.Owner,
            CreatedAt = now
        };

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        context.Workspaces.Add(workspace);
        context.WorkspaceMembers.Add(membership);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return WorkspaceResult.Succeeded(ToSummary(workspace, membership.Role));
    }

    public async Task<WorkspaceResult> GetAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var workspace = await AccessibleWorkspace(userId, workspaceId)
            .Select(member => new WorkspaceSummary(
                member.WorkspaceId,
                member.Workspace.Name,
                member.Workspace.Type,
                member.Role))
            .SingleOrDefaultAsync(cancellationToken);

        return workspace is null
            ? WorkspaceResult.Missing
            : WorkspaceResult.Succeeded(workspace);
    }

    public async Task<WorkspaceResult> RenameAsync(
        Guid userId,
        Guid workspaceId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        var normalized = WorkspaceName.Normalize(name, out var error);
        if (normalized is null)
            return WorkspaceResult.Invalid(error!);

        var membership = await AccessibleWorkspace(userId, workspaceId)
            .Include(member => member.Workspace)
            .SingleOrDefaultAsync(cancellationToken);
        if (membership is null)
            return WorkspaceResult.Missing;

        if (membership.Workspace.Type == WorkspaceTypeValues.Personal)
            return WorkspaceResult.PersonalNameConflict;

        if (membership.Role is not WorkspaceRoleValues.Owner and
            not WorkspaceRoleValues.Admin)
            return WorkspaceResult.Denied;

        membership.Workspace.Name = normalized;
        membership.Workspace.UpdatedAt = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        return WorkspaceResult.Succeeded(ToSummary(
            membership.Workspace,
            membership.Role));
    }

    private IQueryable<WorkspaceMember> AccessibleWorkspace(
        Guid userId,
        Guid workspaceId) => context.WorkspaceMembers.Where(member =>
            member.UserId == userId &&
            member.WorkspaceId == workspaceId &&
            member.RemovedAt == null);

    private static WorkspaceSummary ToSummary(
        Workspace workspace,
        string role) => new(
            workspace.Id,
            workspace.Name,
            workspace.Type,
            role);
}
