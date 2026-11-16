using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces;
using TempoDesk.Infrastructure.Workspaces.Persistence;
using TempoDesk.Tests.Database;

namespace TempoDesk.Tests.Workspaces;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class WorkspaceInvitationTests(
    PostgreSqlFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
    private WorkspaceScenario scenario = null!;

    public async Task InitializeAsync()
    {
        await database.ResetAsync();
        scenario = await WorkspaceScenario.CreateAsync(database, Now);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task IssueByOwnerNormalizesEmailAndPersistsOnlyHash()
    {
        await using var context = database.CreateContext();
        var sender = new CapturingInvitationSender();
        var service = CreateService(context, sender);

        var result = await service.InviteAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            "  Invitee@Example.com ",
            WorkspaceRoles.Member);

        Assert.Equal(WorkspaceMembershipResultStatus.Success, result.Status);
        Assert.NotNull(result.Invitation);
        Assert.Equal("Invitee@Example.com", result.Invitation.Email);
        Assert.Equal(Now.AddDays(7), result.Invitation.ExpiresAt);
        var message = Assert.Single(sender.Messages);
        Assert.Equal("Invitee@Example.com", message.Email);
        Assert.Equal("Delivery Team", message.WorkspaceName);
        Assert.Equal(Now.AddDays(7), message.ExpiresAt);
        Assert.Equal(43, message.Token.Length);

        var stored = await context.WorkspaceInvitations.AsNoTracking()
            .SingleAsync();
        Assert.Equal("INVITEE@EXAMPLE.COM", stored.NormalizedEmail);
        Assert.Equal(Hash(message.Token), stored.TokenHash);
        Assert.Matches("^[0-9a-f]{64}$", stored.TokenHash);
        Assert.DoesNotContain(message.Token, stored.TokenHash,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminCannotIssueAdminInvitation()
    {
        await using var context = database.CreateContext();
        var sender = new CapturingInvitationSender();
        var service = CreateService(context, sender);

        var result = await service.InviteAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            "invitee@example.com",
            WorkspaceRoles.Admin);

        Assert.Equal(WorkspaceMembershipResultStatus.Forbidden, result.Status);
        Assert.Empty(sender.Messages);
        Assert.Empty(await context.WorkspaceInvitations.ToListAsync());
    }

    [Fact]
    public async Task ActiveInvitationAndMembershipReturnConflict()
    {
        await using var context = database.CreateContext();
        var sender = new CapturingInvitationSender();
        var service = CreateService(context, sender);

        var first = await service.InviteAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            "invitee@example.com",
            WorkspaceRoles.Viewer);
        var duplicate = await service.InviteAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            "INVITEE@example.com",
            WorkspaceRoles.Member);
        var existingMember = await service.InviteAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            "admin@example.com",
            WorkspaceRoles.Member);

        Assert.Equal(WorkspaceMembershipResultStatus.Success, first.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Conflict,
            duplicate.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Conflict,
            existingMember.Status);
        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task ExpiredInvitationIsCancelledAndReplaced()
    {
        await using var context = database.CreateContext();
        var expired = new WorkspaceInvitation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.TeamWorkspaceId,
            InvitedByUserId = scenario.OwnerUserId,
            Email = "invitee@example.com",
            NormalizedEmail = "INVITEE@EXAMPLE.COM",
            Role = WorkspaceRoles.Viewer,
            TokenHash = new string('a', 64),
            CreatedAt = Now.AddDays(-8),
            ExpiresAt = Now.AddDays(-1)
        };
        context.WorkspaceInvitations.Add(expired);
        await context.SaveChangesAsync();
        var sender = new CapturingInvitationSender();
        var service = CreateService(context, sender);

        var result = await service.InviteAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            "Invitee@example.com",
            WorkspaceRoles.Member);

        Assert.Equal(WorkspaceMembershipResultStatus.Success, result.Status);
        context.ChangeTracker.Clear();
        var invitations = await context.WorkspaceInvitations
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        Assert.Equal(Now, invitations[0].CancelledAt);
        Assert.Null(invitations[1].CancelledAt);
        Assert.Equal(Now.AddDays(7), invitations[1].ExpiresAt);
    }

    [Fact]
    public async Task PersonalWorkspaceAndInvalidInputAreRejected()
    {
        await using var context = database.CreateContext();
        var sender = new CapturingInvitationSender();
        var service = CreateService(context, sender);

        var personal = await service.InviteAsync(
            scenario.OwnerUserId,
            scenario.PersonalWorkspaceId,
            "invitee@example.com",
            WorkspaceRoles.Member);
        var invalidEmail = await service.InviteAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            "not-an-email",
            WorkspaceRoles.Member);
        var invalidRole = await service.InviteAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            "invitee@example.com",
            WorkspaceRoles.Owner);

        Assert.Equal(WorkspaceMembershipResultStatus.Conflict,
            personal.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.InvalidRequest,
            invalidEmail.Status);
        Assert.Equal("email", invalidEmail.ValidationField);
        Assert.Equal(WorkspaceMembershipResultStatus.InvalidRequest,
            invalidRole.Status);
        Assert.Equal("role", invalidRole.ValidationField);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task ListsOnlyActiveMembersAndPendingInvitations()
    {
        await using var context = database.CreateContext();
        var viewer = await context.WorkspaceMembers.SingleAsync(member =>
            member.WorkspaceId == scenario.TeamWorkspaceId &&
            member.UserId == scenario.ViewerUserId);
        viewer.RemovedAt = Now;
        context.WorkspaceInvitations.AddRange(
            Invitation("pending@example.com", WorkspaceRoles.Member,
                new string('b', 64), Now, Now.AddDays(1)),
            Invitation("expired@example.com", WorkspaceRoles.Viewer,
                new string('c', 64), Now.AddDays(-8), Now.AddDays(-1)),
            Invitation("cancelled@example.com", WorkspaceRoles.Member,
                new string('d', 64), Now, Now.AddDays(1), cancelledAt: Now),
            Invitation("accepted@example.com", WorkspaceRoles.Member,
                new string('e', 64), Now, Now.AddDays(1), acceptedAt: Now));
        await context.SaveChangesAsync();
        var service = CreateService(context, new CapturingInvitationSender());

        var members = await service.ListMembersAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId);
        var invitations = await service.ListInvitationsAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId);

        Assert.Equal(WorkspaceMembershipResultStatus.Success, members.Status);
        Assert.Equal(3, Assert.IsAssignableFrom<
            IReadOnlyList<WorkspaceMemberSummary>>(members.Members).Count);
        var pending = Assert.Single(Assert.IsAssignableFrom<
            IReadOnlyList<WorkspaceInvitationSummary>>(
                invitations.Invitations));
        Assert.Equal("pending@example.com", pending.Email);
    }

    [Fact]
    public async Task CancellationUsesPersistedInvitationRole()
    {
        await using var context = database.CreateContext();
        var memberInvitation = Invitation(
            "member-invite@example.com",
            WorkspaceRoles.Member,
            new string('f', 64),
            Now,
            Now.AddDays(1));
        var adminInvitation = Invitation(
            "admin-invite@example.com",
            WorkspaceRoles.Admin,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            Now,
            Now.AddDays(1));
        context.WorkspaceInvitations.AddRange(
            memberInvitation, adminInvitation);
        await context.SaveChangesAsync();
        var service = CreateService(context, new CapturingInvitationSender());

        var memberResult = await service.CancelInvitationAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            memberInvitation.Id);
        var deniedAdmin = await service.CancelInvitationAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            adminInvitation.Id);
        var ownerResult = await service.CancelInvitationAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            adminInvitation.Id);
        var repeated = await service.CancelInvitationAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            adminInvitation.Id);

        Assert.Equal(WorkspaceMembershipResultStatus.Success,
            memberResult.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Forbidden,
            deniedAdmin.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Success,
            ownerResult.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.NotFound,
            repeated.Status);
    }

    private static WorkspaceMembershipService CreateService(
        TempoDeskIdentityDbContext context,
        IWorkspaceInvitationSender sender) => new(
            context,
            new WorkspaceAuthorizationService(context),
            new UpperInvariantLookupNormalizer(),
            sender,
            new FixedTimeProvider(Now));

    private static string Hash(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private WorkspaceInvitation Invitation(
        string email,
        string role,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? acceptedAt = null,
        DateTimeOffset? cancelledAt = null) => new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.TeamWorkspaceId,
            InvitedByUserId = scenario.OwnerUserId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = role,
            TokenHash = tokenHash,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            AcceptedAt = acceptedAt,
            CancelledAt = cancelledAt
        };

    private sealed class CapturingInvitationSender : IWorkspaceInvitationSender
    {
        public List<WorkspaceInvitationEmail> Messages { get; } = [];

        public Task<bool> SendAsync(
            WorkspaceInvitationEmail message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record WorkspaceScenario(
        Guid OwnerUserId,
        Guid AdminUserId,
        Guid MemberUserId,
        Guid ViewerUserId,
        Guid TeamWorkspaceId,
        Guid PersonalWorkspaceId)
    {
        public static async Task<WorkspaceScenario> CreateAsync(
            PostgreSqlFixture database,
            DateTimeOffset now)
        {
            var owner = User("owner@example.com", now);
            var admin = User("admin@example.com", now);
            var member = User("member@example.com", now);
            var viewer = User("viewer@example.com", now);
            var team = Workspace(
                "Delivery Team", WorkspaceTypeValues.Team, owner.Id, now);
            var personal = Workspace(
                "My Workspace", WorkspaceTypeValues.Personal, owner.Id, now);

            await using var context = database.CreateContext();
            await using var transaction = await context.Database
                .BeginTransactionAsync();
            context.Users.AddRange(owner, admin, member, viewer);
            context.Workspaces.AddRange(team, personal);
            context.WorkspaceMembers.AddRange(
                Membership(team.Id, owner.Id, WorkspaceRoles.Owner, now),
                Membership(team.Id, admin.Id, WorkspaceRoles.Admin, now),
                Membership(team.Id, member.Id, WorkspaceRoles.Member, now),
                Membership(team.Id, viewer.Id, WorkspaceRoles.Viewer, now),
                Membership(personal.Id, owner.Id, WorkspaceRoles.Owner, now));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new WorkspaceScenario(
                owner.Id,
                admin.Id,
                member.Id,
                viewer.Id,
                team.Id,
                personal.Id);
        }

        private static ApplicationUser User(
            string email,
            DateTimeOffset now) => new()
            {
                Id = Guid.NewGuid(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                CreatedAt = now,
                UpdatedAt = now
            };

        private static Workspace Workspace(
            string name,
            string type,
            Guid ownerUserId,
            DateTimeOffset now) => new()
            {
                Id = Guid.NewGuid(),
                Name = name,
                Type = type,
                OwnerUserId = ownerUserId,
                CreatedAt = now,
                UpdatedAt = now
            };

        private static WorkspaceMember Membership(
            Guid workspaceId,
            Guid userId,
            string role,
            DateTimeOffset now) => new()
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                UserId = userId,
                Role = role,
                CreatedAt = now
            };
    }
}
