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
}
