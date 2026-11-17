using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.WorkItems;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.WorkItems;
using TempoDesk.Infrastructure.WorkItems.Persistence;
using TempoDesk.Infrastructure.Workspaces;
using TempoDesk.Tests.Database;

namespace TempoDesk.Tests.WorkItems;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class ProjectServiceTests(
    PostgreSqlFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(WorkspaceRoles.Owner, true)]
    [InlineData(WorkspaceRoles.Admin, true)]
    [InlineData(WorkspaceRoles.Member, false)]
    [InlineData(WorkspaceRoles.Viewer, false)]
    public async Task CreateProjectFollowsPermissionMatrix(
        string role,
        bool allowed)
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        await using var context = database.CreateContext();
        var service = Service(context);

        var result = await service.CreateAsync(
            scenario.UserForRole(role),
            scenario.TeamWorkspaceId,
            " Delivery API ",
            " Backend ");

        Assert.Equal(
            allowed ? WorkItemResultStatus.Success :
                WorkItemResultStatus.Forbidden,
            result.Status);
        Assert.Equal(allowed, await context.Projects.AnyAsync());
    }

    [Fact]
    public async Task ListAndReadExcludeArchivedProjects()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        await using var context = database.CreateContext();
        var service = Service(context);
        var active = await service.CreateAsync(
            scenario.OwnerUserId, scenario.TeamWorkspaceId, "Active", null);
        var archived = await service.CreateAsync(
            scenario.OwnerUserId, scenario.TeamWorkspaceId, "Archived", null);
        await service.ArchiveAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            archived.Value!.Id);

        var list = await service.ListAsync(
            scenario.ViewerUserId, scenario.TeamWorkspaceId);
        var read = await service.GetAsync(
            scenario.ViewerUserId,
            scenario.TeamWorkspaceId,
            archived.Value.Id);

        Assert.Equal(WorkItemResultStatus.Success, list.Status);
        Assert.Equal(active.Value!.Id, Assert.Single(list.Value!).Id);
        Assert.Equal(WorkItemResultStatus.NotFound, read.Status);
    }

    [Fact]
    public async Task CrossWorkspaceProjectIdReturnsNotFound()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        var foreign = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.OtherWorkspaceId,
            Name = "Foreign",
            CreatedAt = Now,
            UpdatedAt = Now
        };
        await using var context = database.CreateContext();
        context.Projects.Add(foreign);
        await context.SaveChangesAsync();

        var result = await Service(context).GetAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            foreign.Id);

        Assert.Equal(WorkItemResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task PatchDistinguishesOmittedDescriptionFromExplicitNull()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        await using var context = database.CreateContext();
        var service = Service(context);
        var created = await service.CreateAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            "Delivery",
            "Keep me");

        var renamed = await service.UpdateAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            created.Value!.Id,
            new(true, "Renamed"),
            default);
        var cleared = await service.UpdateAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            created.Value.Id,
            default,
            new(true, null));

        Assert.Equal("Keep me", renamed.Value!.Description);
        Assert.Null(cleared.Value!.Description);
    }

    private static ProjectService Service(
        TempoDesk.Infrastructure.Identity.TempoDeskIdentityDbContext context) =>
        new(
            context,
            new WorkspaceAuthorizationService(context),
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
