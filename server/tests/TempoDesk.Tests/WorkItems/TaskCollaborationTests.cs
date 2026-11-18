using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.WorkItems;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.WorkItems;
using TempoDesk.Infrastructure.WorkItems.Persistence;
using TempoDesk.Infrastructure.Workspaces;
using TempoDesk.Tests.Database;

namespace TempoDesk.Tests.WorkItems;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class TaskCollaborationTests(
    PostgreSqlFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 17, 1, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AssigneesMustBeDistinctActiveWorkspaceMembers()
    {
        var (scenario, taskId) = await CreateTaskAsync();
        await using var context = database.CreateContext();
        var service = Service(context);

        var assigned = await service.ReplaceAssigneesAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            taskId,
            [scenario.MemberUserId, scenario.ViewerUserId,
                scenario.MemberUserId]);
        var invalid = await service.ReplaceAssigneesAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            taskId,
            [scenario.UnrelatedUserId]);
        var viewerMembership = await context.WorkspaceMembers.SingleAsync(
            member => member.WorkspaceId == scenario.TeamWorkspaceId &&
                member.UserId == scenario.ViewerUserId);
        viewerMembership.RemovedAt = Now;
        await context.SaveChangesAsync();
        var removed = await service.ReplaceAssigneesAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            taskId,
            [scenario.ViewerUserId]);

        Assert.Equal(WorkItemResultStatus.Success, assigned.Status);
        Assert.Equal(2, assigned.Value!.Assignees.Count);
        Assert.Equal(WorkItemResultStatus.Invalid, invalid.Status);
        Assert.Contains("assigneeIds", invalid.Errors!.Keys);
        Assert.Equal(WorkItemResultStatus.Invalid, removed.Status);
    }

    [Fact]
    public async Task LabelNamesReuseCaseInsensitiveWorkspaceLabel()
    {
        var (scenario, taskId) = await CreateTaskAsync();
        await using var context = database.CreateContext();

        var result = await Service(context).ReplaceLabelsAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            taskId,
            [" Backend ", "backend"]);

        Assert.Equal(WorkItemResultStatus.Success, result.Status);
        Assert.Single(result.Value!.Labels);
        Assert.Equal("Backend", result.Value.Labels[0].Name);
        Assert.Equal(1, await context.Labels.CountAsync());
    }

    [Fact]
    public async Task CommentAuthorComesFromAuthenticatedActor()
    {
        var (scenario, taskId) = await CreateTaskAsync();
        await using var context = database.CreateContext();
        var service = Service(context);

        var added = await service.AddCommentAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            taskId,
            " Ready ");
        var listed = await service.ListCommentsAsync(
            scenario.ViewerUserId,
            scenario.TeamWorkspaceId,
            taskId);

        Assert.Equal(scenario.MemberUserId, added.Value!.AuthorUserId);
        Assert.Equal("Ready", added.Value.Body);
        Assert.Equal(added.Value, Assert.Single(listed.Value!));
    }

    [Fact]
    public async Task ViewerCanListButCannotMutateDetails()
    {
        var (scenario, taskId) = await CreateTaskAsync();
        await using var context = database.CreateContext();
        var service = Service(context);

        var listed = await service.ListActivityAsync(
            scenario.ViewerUserId,
            scenario.TeamWorkspaceId,
            taskId);
        var comment = await service.AddCommentAsync(
            scenario.ViewerUserId,
            scenario.TeamWorkspaceId,
            taskId,
            "Denied");

        Assert.Equal(WorkItemResultStatus.Success, listed.Status);
        Assert.Equal(WorkItemResultStatus.Forbidden, comment.Status);
    }

    [Fact]
    public async Task ActivityUsesServerKnownActorAndInvalidMutationRollsBack()
    {
        var (scenario, taskId) = await CreateTaskAsync();
        await using var context = database.CreateContext();
        var service = Service(context);
        await service.AddCommentAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            taskId,
            "Ready");
        var before = await context.TaskActivityLogs.CountAsync();

        var invalid = await service.ReplaceLabelsAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            taskId,
            [" "]);
        var activity = await service.ListActivityAsync(
            scenario.ViewerUserId,
            scenario.TeamWorkspaceId,
            taskId);

        Assert.Equal(WorkItemResultStatus.Invalid, invalid.Status);
        Assert.Equal(before, await context.TaskActivityLogs.CountAsync());
        Assert.Contains(activity.Value!, item =>
            item.EventType == TaskActivityTypes.CommentAdded &&
            item.ActorUserId == scenario.AdminUserId);
    }

    [Fact]
    public async Task MembershipRemovalSerializesWithAssignmentReplacement()
    {
        var (scenario, taskId) = await CreateTaskAsync();
        await using var removal = database.CreateContext();
        await using var transaction = await removal.Database
            .BeginTransactionAsync();
        await removal.Workspaces.FromSqlInterpolated($"""
            SELECT * FROM public.workspaces
            WHERE id = {scenario.TeamWorkspaceId}
            FOR UPDATE
            """).SingleAsync();
        var membership = await removal.WorkspaceMembers.SingleAsync(member =>
            member.WorkspaceId == scenario.TeamWorkspaceId &&
            member.UserId == scenario.ViewerUserId);
        membership.RemovedAt = Now;
        await removal.SaveChangesAsync();

        await using var assignment = database.CreateContext();
        var pending = Service(assignment).ReplaceAssigneesAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            taskId,
            [scenario.ViewerUserId]);
        var completedBeforeRemoval = await Task.WhenAny(
            pending, Task.Delay(TimeSpan.FromMilliseconds(250))) == pending;

        Assert.False(completedBeforeRemoval);
        await transaction.CommitAsync();
        var result = await pending;
        Assert.Equal(WorkItemResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task ConcurrentAssigneeReplacementsDoNotMergeSets()
    {
        var (scenario, taskId) = await CreateTaskAsync();
        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var first = Service(firstContext).ReplaceAssigneesAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            taskId,
            [scenario.MemberUserId]);
        var second = Service(secondContext).ReplaceAssigneesAsync(
            scenario.AdminUserId,
            scenario.TeamWorkspaceId,
            taskId,
            [scenario.ViewerUserId]);

        var results = await Task.WhenAll(first, second);

        Assert.All(results, result =>
            Assert.Equal(WorkItemResultStatus.Success, result.Status));
        await using var verification = database.CreateContext();
        var assignees = await verification.TaskAssignees
            .Where(link => link.TaskId == taskId)
            .Select(link => link.UserId)
            .ToListAsync();
        Assert.Single(assignees);
        Assert.Contains(assignees[0], new[]
        {
            scenario.MemberUserId,
            scenario.ViewerUserId
        });
    }

    private async Task<(WorkItemScenario Scenario, Guid TaskId)>
        CreateTaskAsync()
    {
        var scenario = await WorkItemScenario.CreateAsync(database, Now);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.TeamWorkspaceId,
            Name = "Delivery",
            CreatedAt = Now,
            UpdatedAt = Now
        };
        await using var context = database.CreateContext();
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        var task = await Service(context).CreateAsync(
            scenario.MemberUserId,
            scenario.TeamWorkspaceId,
            project.Id,
            new TaskMutation(
                "Task", null, WorkItemPriorityValues.None,
                WorkItemStatusValues.Todo, null, null, null, null));
        return (scenario, task.Value!.Id);
    }

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
