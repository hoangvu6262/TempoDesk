using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces.Persistence;
using TempoDesk.Tests.Database;

namespace TempoDesk.Tests.WorkItems;

internal sealed record WorkItemScenario(
    Guid OwnerUserId,
    Guid AdminUserId,
    Guid MemberUserId,
    Guid ViewerUserId,
    Guid UnrelatedUserId,
    Guid TeamWorkspaceId,
    Guid PersonalWorkspaceId,
    Guid OtherWorkspaceId)
{
    public Guid UserForRole(string role) => role switch
    {
        WorkspaceRoles.Owner => OwnerUserId,
        WorkspaceRoles.Admin => AdminUserId,
        WorkspaceRoles.Member => MemberUserId,
        WorkspaceRoles.Viewer => ViewerUserId,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static async Task<WorkItemScenario> CreateAsync(
        PostgreSqlFixture database,
        DateTimeOffset now)
    {
        var owner = User("owner@example.com", now);
        var admin = User("admin@example.com", now);
        var member = User("member@example.com", now);
        var viewer = User("viewer@example.com", now);
        var unrelated = User("unrelated@example.com", now);
        var team = Workspace(
            "Delivery", WorkspaceTypeValues.Team, owner.Id, now);
        var personal = Workspace(
            "Personal", WorkspaceTypeValues.Personal, owner.Id, now);
        var other = Workspace(
            "Other", WorkspaceTypeValues.Team, unrelated.Id, now);

        await using var context = database.CreateContext();
        context.Users.AddRange(owner, admin, member, viewer, unrelated);
        context.Workspaces.AddRange(team, personal, other);
        context.WorkspaceMembers.AddRange(
            Membership(team.Id, owner.Id, WorkspaceRoles.Owner, now),
            Membership(team.Id, admin.Id, WorkspaceRoles.Admin, now),
            Membership(team.Id, member.Id, WorkspaceRoles.Member, now),
            Membership(team.Id, viewer.Id, WorkspaceRoles.Viewer, now),
            Membership(personal.Id, owner.Id, WorkspaceRoles.Owner, now),
            Membership(other.Id, unrelated.Id, WorkspaceRoles.Owner, now));
        await context.SaveChangesAsync();

        return new(
            owner.Id,
            admin.Id,
            member.Id,
            viewer.Id,
            unrelated.Id,
            team.Id,
            personal.Id,
            other.Id);
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
