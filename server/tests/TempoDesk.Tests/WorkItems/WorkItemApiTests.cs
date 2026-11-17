using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Api.Controllers;
using TempoDesk.Application.WorkItems;

namespace TempoDesk.Tests.WorkItems;

public sealed class WorkItemApiTests
{
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    [Fact]
    public async Task ProjectCreateUsesAuthenticatedActorAndReturnsSafeDto()
    {
        var project = new ProjectSummary(
            Guid.NewGuid(),
            WorkspaceId,
            "Delivery",
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var service = new FakeProjectService
        {
            Result = new(WorkItemResultStatus.Success, project)
        };
        var controller = Authenticate(new ProjectsController(service));

        var action = await controller.Create(
            WorkspaceId,
            new CreateProjectRequestDto("Delivery", null),
            CancellationToken.None);

        Assert.Equal(ActorUserId, service.ActorUserId);
        var created = Assert.IsType<CreatedAtActionResult>(action);
        var json = JsonSerializer.Serialize(created.Value);
        Assert.DoesNotContain("workspace", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tasks", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectControllerRequiresAuthorization() =>
        Assert.NotNull(Attribute.GetCustomAttribute(
            typeof(ProjectsController),
            typeof(AuthorizeAttribute)));

    [Fact]
    public async Task TaskReadSetsQuotedRowVersionEtag()
    {
        var task = TaskSummary(7);
        var service = new FakeTaskService
        {
            Result = new(WorkItemResultStatus.Success, task)
        };
        var controller = Authenticate(new TasksController(service));

        var action = await controller.Read(
            WorkspaceId, task.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(action);
        Assert.Equal("\"7\"", controller.Response.Headers.ETag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("W/\"7\"")]
    [InlineData("\"0\"")]
    [InlineData("*")]
    public async Task TaskUpdateRejectsInvalidIfMatch(string? value)
    {
        var service = new FakeTaskService
        {
            Result = new(WorkItemResultStatus.Success, TaskSummary(7))
        };
        var controller = Authenticate(new TasksController(service));
        if (value is not null)
            controller.Request.Headers.IfMatch = value;

        var action = await controller.Update(
            WorkspaceId,
            service.Result.Value!.Id,
            new UpdateTaskRequestDto(),
            CancellationToken.None);

        var invalid = Assert.IsType<ObjectResult>(action);
        Assert.IsType<ValidationProblemDetails>(invalid.Value);
        Assert.False(service.UpdateCalled);
    }

    [Fact]
    public void TaskControllerRequiresAuthorization() =>
        Assert.NotNull(Attribute.GetCustomAttribute(
            typeof(TasksController),
            typeof(AuthorizeAttribute)));

    [Fact]
    public async Task AddCommentUsesAuthenticatedActor()
    {
        var comment = new TaskCommentSummary(
            Guid.NewGuid(),
            ActorUserId,
            "member@example.com",
            "Ready",
            DateTimeOffset.UtcNow);
        var service = new FakeTaskService
        {
            CommentResult = new(WorkItemResultStatus.Success, comment)
        };
        var controller = Authenticate(new TaskDetailsController(service));

        var action = await controller.AddComment(
            WorkspaceId,
            Guid.NewGuid(),
            new AddTaskCommentRequestDto("Ready"),
            CancellationToken.None);

        Assert.Equal(ActorUserId, service.ActorUserId);
        Assert.IsType<CreatedAtActionResult>(action);
    }

    [Fact]
    public void TaskDetailsControllerRequiresAuthorization() =>
        Assert.NotNull(Attribute.GetCustomAttribute(
            typeof(TaskDetailsController),
            typeof(AuthorizeAttribute)));

    [Fact]
    public async Task MyWorkUsesAuthenticatedActorAndTimeZone()
    {
        var summary = new MyWorkSummary(
            [], [], [], [], [], []);
        var service = new FakeMyWorkService
        {
            Result = new(WorkItemResultStatus.Success, summary)
        };
        var controller = Authenticate(new MyWorkController(service));

        var action = await controller.Get(
            "Asia/Shanghai", CancellationToken.None);

        Assert.Equal(ActorUserId, service.ActorUserId);
        Assert.Equal("Asia/Shanghai", service.TimeZone);
        Assert.IsType<OkObjectResult>(action);
    }

    [Fact]
    public void MyWorkControllerRequiresAuthorization() =>
        Assert.NotNull(Attribute.GetCustomAttribute(
            typeof(MyWorkController),
            typeof(AuthorizeAttribute)));

    private static T Authenticate<T>(T controller) where T : ControllerBase
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, ActorUserId.ToString())],
            "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
        return controller;
    }

    private sealed class FakeProjectService : IProjectService
    {
        public WorkItemResult<ProjectSummary> Result { get; init; } =
            new(WorkItemResultStatus.NotFound);
        public Guid ActorUserId { get; private set; }

        public Task<WorkItemResult<IReadOnlyList<ProjectSummary>>> ListAsync(
            Guid actorUserId, Guid workspaceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkItemResult<IReadOnlyList<ProjectSummary>>(
                Result.Status,
                Result.Value is null ? null : [Result.Value],
                Result.Errors));

        public Task<WorkItemResult<ProjectSummary>> CreateAsync(
            Guid actorUserId, Guid workspaceId, string? name,
            string? description,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkItemResult<ProjectSummary>> GetAsync(
            Guid actorUserId, Guid workspaceId, Guid projectId,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkItemResult<ProjectSummary>> UpdateAsync(
            Guid actorUserId, Guid workspaceId, Guid projectId,
            FieldUpdate<string> name, FieldUpdate<string?> description,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkItemResult<ProjectSummary>> ArchiveAsync(
            Guid actorUserId, Guid workspaceId, Guid projectId,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        private Task<WorkItemResult<ProjectSummary>> Return(Guid actorUserId)
        {
            ActorUserId = actorUserId;
            return Task.FromResult(Result);
        }
    }

    private static TaskSummary TaskSummary(long rowVersion) => new(
        Guid.NewGuid(),
        WorkspaceId,
        Guid.NewGuid(),
        null,
        "Task",
        null,
        WorkItemPriorityValues.None,
        WorkItemStatusValues.Todo,
        null,
        null,
        null,
        WorkItemSourceTypeValues.Manual,
        1000m,
        rowVersion,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        [],
        []);

    private sealed class FakeTaskService : ITaskService
    {
        public WorkItemResult<TaskSummary> Result { get; init; } =
            new(WorkItemResultStatus.NotFound);
        public bool UpdateCalled { get; private set; }
        public Guid ActorUserId { get; private set; }
        public WorkItemResult<TaskCommentSummary> CommentResult { get; init; } =
            new(WorkItemResultStatus.NotFound);

        public Task<WorkItemResult<IReadOnlyList<TaskSummary>>> ListAsync(
            Guid actorUserId, Guid workspaceId, Guid projectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkItemResult<IReadOnlyList<TaskSummary>>(
                Result.Status,
                Result.Value is null ? null : [Result.Value],
                Result.Errors));

        public Task<WorkItemResult<TaskSummary>> CreateAsync(
            Guid actorUserId, Guid workspaceId, Guid projectId,
            TaskMutation task,
            CancellationToken cancellationToken = default) => Return();

        public Task<WorkItemResult<TaskSummary>> GetAsync(
            Guid actorUserId, Guid workspaceId, Guid taskId,
            CancellationToken cancellationToken = default) => Return();

        public Task<WorkItemResult<TaskSummary>> UpdateAsync(
            Guid actorUserId, Guid workspaceId, Guid taskId,
            TaskPatch patch, long rowVersion,
            CancellationToken cancellationToken = default)
        {
            UpdateCalled = true;
            return Return();
        }

        public Task<WorkItemResult<TaskSummary>> DeleteAsync(
            Guid actorUserId, Guid workspaceId, Guid taskId, long rowVersion,
            CancellationToken cancellationToken = default) => Return();

        public Task<WorkItemResult<TaskSummary>> RestoreAsync(
            Guid actorUserId, Guid workspaceId, Guid taskId, long rowVersion,
            CancellationToken cancellationToken = default) => Return();

        public Task<WorkItemResult<TaskSummary>> ReplaceAssigneesAsync(
            Guid actorUserId, Guid workspaceId, Guid taskId,
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default) => Return();

        public Task<WorkItemResult<TaskSummary>> ReplaceLabelsAsync(
            Guid actorUserId, Guid workspaceId, Guid taskId,
            IReadOnlyCollection<string?> names,
            CancellationToken cancellationToken = default) => Return();

        public Task<WorkItemResult<IReadOnlyList<TaskCommentSummary>>>
            ListCommentsAsync(
                Guid actorUserId, Guid workspaceId, Guid taskId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkItemResult<TaskCommentSummary>> AddCommentAsync(
            Guid actorUserId, Guid workspaceId, Guid taskId, string? body,
            CancellationToken cancellationToken = default)
        {
            ActorUserId = actorUserId;
            return Task.FromResult(CommentResult);
        }

        public Task<WorkItemResult<IReadOnlyList<TaskActivitySummary>>>
            ListActivityAsync(
                Guid actorUserId, Guid workspaceId, Guid taskId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private Task<WorkItemResult<TaskSummary>> Return() =>
            Task.FromResult(Result);
    }

    private sealed class FakeMyWorkService : IMyWorkService
    {
        public WorkItemResult<MyWorkSummary> Result { get; init; } =
            new(WorkItemResultStatus.NotFound);
        public Guid ActorUserId { get; private set; }
        public string? TimeZone { get; private set; }

        public Task<WorkItemResult<MyWorkSummary>> GetAsync(
            Guid actorUserId,
            string? timeZone,
            CancellationToken cancellationToken = default)
        {
            ActorUserId = actorUserId;
            TimeZone = timeZone;
            return Task.FromResult(Result);
        }
    }
}
