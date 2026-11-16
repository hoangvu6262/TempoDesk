using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces;
using TempoDesk.Infrastructure.Workspaces.Persistence;
using TempoDesk.Tests.Database;

namespace TempoDesk.Tests.Workspaces;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class WorkspaceMemberLifecycleTests(
    PostgreSqlFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private LifecycleScenario scenario = null!;

    public async Task InitializeAsync()
    {
        await database.ResetAsync();
        scenario = await LifecycleScenario.CreateAsync(database, Now);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AdminChangesMemberViewerButCannotManageAdminOrOwner()
    {
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var member = await service.ChangeRoleAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            scenario.MemberMembershipId,
            WorkspaceRoles.Viewer);
        var admin = await service.ChangeRoleAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            scenario.AdminMembershipId,
            WorkspaceRoles.Member);
        var owner = await service.ChangeRoleAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            scenario.OwnerMembershipId,
            WorkspaceRoles.Admin);

        Assert.Equal(WorkspaceMembershipResultStatus.Success, member.Status);
        Assert.Equal(WorkspaceRoles.Viewer, member.Member?.Role);
        Assert.Equal(WorkspaceMembershipResultStatus.Forbidden, admin.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Forbidden, owner.Status);
    }

    [Fact]
    public async Task OwnerPromotesAndDemotesAdminButCannotAssignOwnerRole()
    {
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var promoted = await service.ChangeRoleAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            scenario.MemberMembershipId,
            WorkspaceRoles.Admin);
        var demoted = await service.ChangeRoleAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            scenario.MemberMembershipId,
            WorkspaceRoles.Member);
        var invalid = await service.ChangeRoleAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            scenario.MemberMembershipId,
            WorkspaceRoles.Owner);

        Assert.Equal(WorkspaceMembershipResultStatus.Success,
            promoted.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Success,
            demoted.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.InvalidRequest,
            invalid.Status);
        Assert.Equal("role", invalid.ValidationField);
    }

    [Fact]
    public async Task RemoveRevokesAccessAndProtectsAdminOwnerAndPersonal()
    {
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var removed = await service.RemoveMemberAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            scenario.MemberMembershipId);
        var adminDenied = await service.RemoveMemberAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            scenario.AdminMembershipId);
        var ownerDenied = await service.RemoveMemberAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            scenario.OwnerMembershipId);
        var personal = await service.RemoveMemberAsync(
            scenario.OwnerUserId,
            scenario.PersonalWorkspaceId,
            scenario.PersonalOwnerMembershipId);

        Assert.Equal(WorkspaceMembershipResultStatus.Success, removed.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Forbidden,
            adminDenied.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Forbidden,
            ownerDenied.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Conflict,
            personal.Status);
        var access = await new WorkspaceAuthorizationService(context)
            .AuthorizeAsync(
                scenario.MemberUserId,
                scenario.TeamWorkspaceId,
                WorkspaceAction.ViewWorkspace);
        Assert.Equal(WorkspaceAuthorizationStatus.NotFound, access.Status);
    }

    [Fact]
    public async Task LeaveRevokesAccessButOwnerAndPersonalCannotLeave()
    {
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var left = await service.LeaveAsync(
            scenario.AdminUserId, scenario.TeamWorkspaceId);
        var owner = await service.LeaveAsync(
            scenario.OwnerUserId, scenario.TeamWorkspaceId);
        var personal = await service.LeaveAsync(
            scenario.OwnerUserId, scenario.PersonalWorkspaceId);

        Assert.Equal(WorkspaceMembershipResultStatus.Success, left.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Forbidden, owner.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Conflict,
            personal.Status);
        var access = await new WorkspaceAuthorizationService(context)
            .AuthorizeAsync(
                scenario.AdminUserId,
                scenario.TeamWorkspaceId,
                WorkspaceAction.ViewWorkspace);
        Assert.Equal(WorkspaceAuthorizationStatus.NotFound, access.Status);
    }

    [Fact]
    public async Task TransferUpdatesWorkspaceAndBothRolesAtomically()
    {
        await using var context = database.CreateContext();
        var service = CreateService(context);

        var result = await service.TransferOwnershipAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            scenario.MemberMembershipId);

        Assert.Equal(WorkspaceMembershipResultStatus.Success, result.Status);
        context.ChangeTracker.Clear();
        var workspace = await context.Workspaces.SingleAsync(item =>
            item.Id == scenario.TeamWorkspaceId);
        var memberships = await context.WorkspaceMembers
            .Where(member => member.WorkspaceId == scenario.TeamWorkspaceId &&
                member.RemovedAt == null)
            .ToListAsync();
        Assert.Equal(scenario.MemberUserId, workspace.OwnerUserId);
        Assert.Equal(WorkspaceRoles.Admin, memberships.Single(member =>
            member.UserId == scenario.OwnerUserId).Role);
        Assert.Equal(WorkspaceRoles.Owner, memberships.Single(member =>
            member.UserId == scenario.MemberUserId).Role);
        Assert.Single(memberships, member =>
            member.Role == WorkspaceRoles.Owner);
    }

    [Fact]
    public async Task TransferRejectsNonOwnerMissingRemovedAndPersonalTargets()
    {
        await using var context = database.CreateContext();
        var viewer = await context.WorkspaceMembers.SingleAsync(member =>
            member.Id == scenario.ViewerMembershipId);
        viewer.RemovedAt = Now;
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var nonOwner = await service.TransferOwnershipAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            scenario.MemberMembershipId);
        var missing = await service.TransferOwnershipAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            Guid.NewGuid());
        var removed = await service.TransferOwnershipAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            scenario.ViewerMembershipId);
        var personal = await service.TransferOwnershipAsync(
            scenario.OwnerUserId,
            scenario.PersonalWorkspaceId,
            scenario.MemberMembershipId);

        Assert.Equal(WorkspaceMembershipResultStatus.Forbidden,
            nonOwner.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.NotFound, missing.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.NotFound, removed.Status);
        Assert.Equal(WorkspaceMembershipResultStatus.Conflict,
            personal.Status);
    }

    [Fact]
    public async Task ConcurrentTransfersLeaveExactlyOneValidOwner()
    {
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var firstService = CreateService(firstContext);
        var secondService = CreateService(secondContext);

        var results = await Task.WhenAll(
            firstService.TransferOwnershipAsync(
                scenario.OwnerUserId,
                scenario.TeamWorkspaceId,
                scenario.MemberMembershipId),
            secondService.TransferOwnershipAsync(
                scenario.OwnerUserId,
                scenario.TeamWorkspaceId,
                scenario.ViewerMembershipId));

        Assert.Single(results, result =>
            result.Status == WorkspaceMembershipResultStatus.Success);
        Assert.Single(results, result =>
            result.Status == WorkspaceMembershipResultStatus.Forbidden);
        await using var verification = database.CreateContext();
        var workspace = await verification.Workspaces
            .AsNoTracking()
            .SingleAsync(item => item.Id == scenario.TeamWorkspaceId);
        var owner = await verification.WorkspaceMembers
            .AsNoTracking()
            .SingleAsync(member =>
                member.WorkspaceId == scenario.TeamWorkspaceId &&
                member.Role == WorkspaceRoles.Owner &&
                member.RemovedAt == null);
        Assert.Equal(workspace.OwnerUserId, owner.UserId);
    }

    private static WorkspaceMembershipService CreateService(
        TempoDeskIdentityDbContext context) => new(
            context,
            new WorkspaceAuthorizationService(context),
            new UpperInvariantLookupNormalizer(),
            new NoOpInvitationSender(),
            new FixedTimeProvider(Now));

    private sealed class NoOpInvitationSender : IWorkspaceInvitationSender
    {
        public Task<bool> SendAsync(
            WorkspaceInvitationEmail message,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record LifecycleScenario(
        Guid OwnerUserId,
        Guid AdminUserId,
        Guid MemberUserId,
        Guid TeamWorkspaceId,
        Guid PersonalWorkspaceId,
        Guid OwnerMembershipId,
        Guid AdminMembershipId,
        Guid MemberMembershipId,
        Guid ViewerMembershipId,
        Guid PersonalOwnerMembershipId)
    {
        public static async Task<LifecycleScenario> CreateAsync(
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
            var ownerMembership = Membership(
                team.Id, owner.Id, WorkspaceRoles.Owner, now);
            var adminMembership = Membership(
                team.Id, admin.Id, WorkspaceRoles.Admin, now);
            var memberMembership = Membership(
                team.Id, member.Id, WorkspaceRoles.Member, now);
            var viewerMembership = Membership(
                team.Id, viewer.Id, WorkspaceRoles.Viewer, now);
            var personalMembership = Membership(
                personal.Id, owner.Id, WorkspaceRoles.Owner, now);

            await using var context = database.CreateContext();
            await using var transaction = await context.Database
                .BeginTransactionAsync();
            context.Users.AddRange(owner, admin, member, viewer);
            context.Workspaces.AddRange(team, personal);
            context.WorkspaceMembers.AddRange(
                ownerMembership,
                adminMembership,
                memberMembership,
                viewerMembership,
                personalMembership);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new LifecycleScenario(
                owner.Id,
                admin.Id,
                member.Id,
                team.Id,
                personal.Id,
                ownerMembership.Id,
                adminMembership.Id,
                memberMembership.Id,
                viewerMembership.Id,
                personalMembership.Id);
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
