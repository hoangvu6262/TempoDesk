using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Api.Controllers;
using TempoDesk.Application.Workspaces;

namespace TempoDesk.Tests.Workspaces;

public sealed class WorkspaceMembershipApiTests
{
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task InviteUsesAuthenticatedActorAndReturnsSafeDto()
    {
        var invitation = new WorkspaceInvitationSummary(
            Guid.NewGuid(),
            "invitee@example.com",
            WorkspaceRoles.Member,
            new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero));
        var service = new FakeMembershipService
        {
            Result = WorkspaceMembershipResult.WithInvitation(invitation)
        };
        var controller = Authenticate(
            new WorkspaceMembershipController(service));

        var action = await controller.Invite(
            WorkspaceId,
            new InviteWorkspaceMemberRequestDto(
                "invitee@example.com", WorkspaceRoles.Member),
            CancellationToken.None);

        Assert.Equal(ActorUserId, service.ActorUserId);
        var created = Assert.IsType<CreatedAtActionResult>(action);
        var dto = Assert.IsType<WorkspaceInvitationResponseDto>(created.Value);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        Assert.Equal(
            ["createdAt", "email", "expiresAt", "id", "role"],
            JsonDocument.Parse(json).RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Order()
                .ToArray());
        Assert.DoesNotContain("token", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MemberResponseContainsOnlyApprovedFields()
    {
        var member = new WorkspaceMemberSummary(
            Guid.NewGuid(),
            "member@example.com",
            WorkspaceRoles.Viewer,
            new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero));
        var service = new FakeMembershipService
        {
            Result = WorkspaceMembershipResult.WithMember(member)
        };
        var controller = Authenticate(
            new WorkspaceMembershipController(service));

        var action = await controller.ChangeRole(
            WorkspaceId,
            member.Id,
            new ChangeWorkspaceMemberRoleRequestDto(WorkspaceRoles.Viewer),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var dto = Assert.IsType<WorkspaceMemberResponseDto>(ok.Value);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        Assert.Equal(
            ["email", "id", "joinedAt", "role"],
            JsonDocument.Parse(json).RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Order()
                .ToArray());
        Assert.DoesNotContain("password", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("concurrencyStamp", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(WorkspaceMembershipResultStatus.InvalidInvitation, 400)]
    [InlineData(WorkspaceMembershipResultStatus.NotFound, 404)]
    [InlineData(WorkspaceMembershipResultStatus.Forbidden, 403)]
    [InlineData(WorkspaceMembershipResultStatus.Conflict, 409)]
    public async Task AcceptanceMapsSafeFailureStatus(
        WorkspaceMembershipResultStatus status,
        int expectedStatus)
    {
        var service = new FakeMembershipService
        {
            Result = new WorkspaceMembershipResult(status)
        };
        var controller = Authenticate(
            new WorkspaceInvitationAcceptanceController(service));

        var action = await controller.Accept(
            new AcceptWorkspaceInvitationRequestDto("not-returned"),
            CancellationToken.None);

        var statusCode = action switch
        {
            ObjectResult { StatusCode: not null } result => result.StatusCode,
            ObjectResult { Value: ValidationProblemDetails } =>
                StatusCodes.Status400BadRequest,
            ObjectResult { Value: ProblemDetails problem } => problem.Status,
            StatusCodeResult result => result.StatusCode,
            _ => null
        };
        Assert.Equal(expectedStatus, statusCode);
        Assert.DoesNotContain("not-returned",
            JsonSerializer.Serialize(action),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ControllersRequireAuthorization()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(
            typeof(WorkspaceMembershipController),
            typeof(AuthorizeAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(
            typeof(WorkspaceInvitationAcceptanceController),
            typeof(AuthorizeAttribute)));
    }

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

    private sealed class FakeMembershipService : IWorkspaceMembershipService
    {
        public WorkspaceMembershipResult Result { get; init; } =
            WorkspaceMembershipResult.Succeeded;
        public Guid ActorUserId { get; private set; }

        public Task<WorkspaceMembershipResult> ListMembersAsync(
            Guid actorUserId, Guid workspaceId,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkspaceMembershipResult> ListInvitationsAsync(
            Guid actorUserId, Guid workspaceId,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkspaceMembershipResult> InviteAsync(
            Guid actorUserId, Guid workspaceId, string? email, string? role,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkspaceMembershipResult> CancelInvitationAsync(
            Guid actorUserId, Guid workspaceId, Guid invitationId,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkspaceMembershipResult> AcceptAsync(
            Guid userId, string? token,
            CancellationToken cancellationToken = default) => Return(userId);

        public Task<WorkspaceMembershipResult> ChangeRoleAsync(
            Guid actorUserId, Guid workspaceId, Guid memberId, string? role,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkspaceMembershipResult> RemoveMemberAsync(
            Guid actorUserId, Guid workspaceId, Guid memberId,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkspaceMembershipResult> LeaveAsync(
            Guid actorUserId, Guid workspaceId,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        public Task<WorkspaceMembershipResult> TransferOwnershipAsync(
            Guid actorUserId, Guid workspaceId, Guid targetMemberId,
            CancellationToken cancellationToken = default) =>
            Return(actorUserId);

        private Task<WorkspaceMembershipResult> Return(Guid actorUserId)
        {
            ActorUserId = actorUserId;
            return Task.FromResult(Result);
        }
    }
}
