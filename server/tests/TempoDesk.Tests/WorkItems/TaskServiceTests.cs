using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.WorkItems;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.WorkItems;
using TempoDesk.Infrastructure.WorkItems.Persistence;
using TempoDesk.Infrastructure.Workspaces;
using TempoDesk.Tests.Database;

namespace TempoDesk.Tests.WorkItems;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class TaskServiceTests(
    PostgreSqlFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MemberCreatesParentAndOneLevelSubtask()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        var projectId = await SeedProjectAsync(
            scenario.TeamWorkspaceId, "Delivery");
        await using var context = database.CreateContext();
        var service = Service(context);

        var parent = await service.CreateAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId, projectId,
            Mutation("Parent"));
        var child = await service.CreateAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId, projectId,
            Mutation("Child", parent.Value!.Id));

        Assert.Equal(WorkItemResultStatus.Success, parent.Status);
        Assert.Equal(WorkItemResultStatus.Success, child.Status);
        Assert.Equal(parent.Value.Id, child.Value!.ParentTaskId);
    }

    [Fact]
    public async Task ViewerCannotCreateOrUpdateTask()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        var projectId = await SeedProjectAsync(
            scenario.TeamWorkspaceId, "Delivery");
        await using var context = database.CreateContext();
        var service = Service(context);

        var result = await service.CreateAsync(
            scenario.ViewerUserId, scenario.TeamWorkspaceId, projectId,
            Mutation("Denied"));

        Assert.Equal(WorkItemResultStatus.Forbidden, result.Status);
    }

    [Theory]
    [InlineData(WorkspaceRoles.Owner, true)]
    [InlineData(WorkspaceRoles.Admin, true)]
    [InlineData(WorkspaceRoles.Member, true)]
    [InlineData(WorkspaceRoles.Viewer, false)]
    public async Task CreateTaskFollowsPermissionMatrix(
        string role,
        bool allowed)
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        var projectId = await SeedProjectAsync(
            scenario.TeamWorkspaceId, "Delivery");
        await using var context = database.CreateContext();

        var result = await Service(context).CreateAsync(
            scenario.UserForRole(role),
            scenario.TeamWorkspaceId,
            projectId,
            Mutation(role));

        Assert.Equal(
            allowed ? WorkItemResultStatus.Success :
                WorkItemResultStatus.Forbidden,
            result.Status);
    }

    [Fact]
    public async Task RemovedMemberAndForeignOrArchivedTaskReturnNotFound()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        var teamProject = await SeedProjectAsync(
            scenario.TeamWorkspaceId, "Team");
        var otherProject = await SeedProjectAsync(
            scenario.OtherWorkspaceId, "Other");
        await using var context = database.CreateContext();
        var teamTask = await Service(context).CreateAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            teamProject,
            Mutation("Team task"));
        var foreignTask = new WorkTask
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.OtherWorkspaceId,
            ProjectId = otherProject,
            CreatedByUserId = scenario.UnrelatedUserId,
            Title = "Foreign",
            SortKey = 1000m,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        context.Tasks.Add(foreignTask);
        var membership = await context.WorkspaceMembers.SingleAsync(member =>
            member.WorkspaceId == scenario.TeamWorkspaceId &&
            member.UserId == scenario.MemberUserId);
        membership.RemovedAt = Now;
        await context.SaveChangesAsync();

        var removed = await Service(context).GetAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            teamTask.Value!.Id);
        var foreign = await Service(context).GetAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            foreignTask.Id);
        var project = await context.Projects.SingleAsync(item =>
            item.Id == teamProject);
        project.ArchivedAt = Now;
        await context.SaveChangesAsync();
        var archived = await Service(context).GetAsync(
            scenario.OwnerUserId,
            scenario.TeamWorkspaceId,
            teamTask.Value.Id);

        Assert.Equal(WorkItemResultStatus.NotFound, removed.Status);
        Assert.Equal(WorkItemResultStatus.NotFound, foreign.Status);
        Assert.Equal(WorkItemResultStatus.NotFound, archived.Status);
    }

    [Fact]
    public async Task ParentFromAnotherProjectOrSubtaskIsInvalid()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        var firstProject = await SeedProjectAsync(
            scenario.TeamWorkspaceId, "First");
        var secondProject = await SeedProjectAsync(
            scenario.TeamWorkspaceId, "Second");
        await using var context = database.CreateContext();
        var service = Service(context);
        var parent = await service.CreateAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId, firstProject,
            Mutation("Parent"));
        var child = await service.CreateAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId, firstProject,
            Mutation("Child", parent.Value!.Id));

        var wrongProject = await service.CreateAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId, secondProject,
            Mutation("Wrong", parent.Value.Id));
        var grandchild = await service.CreateAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId, firstProject,
            Mutation("Grandchild", child.Value!.Id));

        Assert.Equal(WorkItemResultStatus.Invalid, wrongProject.Status);
        Assert.Contains("parentTaskId", wrongProject.Errors!.Keys);
        Assert.Equal(WorkItemResultStatus.Invalid, grandchild.Status);
        Assert.Contains("parentTaskId", grandchild.Errors!.Keys);
    }

    [Fact]
    public async Task StaleUpdateReturnsConflictWithoutOverwriting()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        var projectId = await SeedProjectAsync(
            scenario.TeamWorkspaceId, "Delivery");
        long originalVersion;
        Guid taskId;
        await using (var create = database.CreateContext())
        {
            var result = await Service(create).CreateAsync(
                scenario.MemberUserId, scenario.TeamWorkspaceId, projectId,
                Mutation("Original"));
            taskId = result.Value!.Id;
            originalVersion = result.Value.RowVersion;
            var storedVersion = await create.Tasks.AsNoTracking()
                .Where(task => task.Id == taskId)
                .Select(task => task.RowVersion)
                .SingleAsync();
            Assert.Equal(storedVersion, originalVersion);
        }

        await using (var first = database.CreateContext())
        {
            var result = await Service(first).UpdateAsync(
                scenario.MemberUserId, scenario.TeamWorkspaceId, taskId,
                TitlePatch("First"), originalVersion);
            Assert.Equal(WorkItemResultStatus.Success, result.Status);
        }

        await using (var stale = database.CreateContext())
        {
            var result = await Service(stale).UpdateAsync(
                scenario.MemberUserId, scenario.TeamWorkspaceId, taskId,
                TitlePatch("Stale"), originalVersion);
            Assert.Equal(WorkItemResultStatus.Conflict, result.Status);
            Assert.True(result.CurrentRowVersion > originalVersion);
        }

        await using var verification = database.CreateContext();
        Assert.Equal("First", (await verification.Tasks.SingleAsync(
            task => task.Id == taskId)).Title);
    }

    [Fact]
    public async Task DeleteRestoreUsesTimestampMatchedChildCascade()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        var projectId = await SeedProjectAsync(
            scenario.TeamWorkspaceId, "Delivery");
        await using var context = database.CreateContext();
        var service = Service(context);
        var parent = await service.CreateAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId, projectId,
            Mutation("Parent"));
        var keptDeleted = await service.CreateAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId, projectId,
            Mutation("Deleted first", parent.Value!.Id));
        var cascaded = await service.CreateAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId, projectId,
            Mutation("Cascade", parent.Value.Id));

        await service.DeleteAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId,
            keptDeleted.Value!.Id, keptDeleted.Value.RowVersion);
        var deletedParent = await service.DeleteAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId,
            parent.Value.Id, parent.Value.RowVersion);
        var restored = await service.RestoreAsync(
            scenario.MemberUserId, scenario.TeamWorkspaceId,
            parent.Value.Id, deletedParent.Value!.RowVersion);

        Assert.Equal(WorkItemResultStatus.Success, restored.Status);
        var rows = await context.Tasks.IgnoreQueryFilters()
            .Where(task => task.ProjectId == projectId)
            .ToDictionaryAsync(task => task.Id);
        Assert.Null(rows[parent.Value.Id].DeletedAt);
        Assert.Null(rows[cascaded.Value!.Id].DeletedAt);
        Assert.NotNull(rows[keptDeleted.Value.Id].DeletedAt);
    }

    private async Task<Guid> SeedProjectAsync(
        Guid workspaceId,
        string name)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = name,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        await using var context = database.CreateContext();
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return project.Id;
    }

    private static TaskMutation Mutation(
        string title,
        Guid? parentTaskId = null) => new(
        title,
        null,
        WorkItemPriorityValues.None,
        WorkItemStatusValues.Todo,
        null,
        null,
        null,
        parentTaskId);

    private static TaskPatch TitlePatch(string title) => new(
        new(true, title),
        default,
        default,
        default,
        default,
        default,
        default,
        default);

    private static TaskService Service(TempoDeskIdentityDbContext context) =>
        new(
            context,
            new WorkspaceAuthorizationService(context),
            new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
