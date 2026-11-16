using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Workspaces;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspace-invitations")]
public sealed class WorkspaceInvitationAcceptanceController(
    IWorkspaceMembershipService memberships) : ControllerBase
{
    [HttpPost("accept")]
    public async Task<IActionResult> Accept(
        AcceptWorkspaceInvitationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await memberships.AcceptAsync(
            UserId(), request.Token, cancellationToken);
        return result.Status == WorkspaceMembershipResultStatus.Success
            ? Ok(ToDto(result.Member!))
            : Failure(result);
    }

    private IActionResult Failure(WorkspaceMembershipResult result)
    {
        switch (result.Status)
        {
            case WorkspaceMembershipResultStatus.InvalidInvitation:
                ModelState.AddModelError(
                    "token",
                    "The invitation is invalid or no longer active.");
                return ValidationProblem(ModelState);
            case WorkspaceMembershipResultStatus.InvalidRequest:
                ModelState.AddModelError(
                    result.ValidationField!, result.ValidationError!);
                return ValidationProblem(ModelState);
            case WorkspaceMembershipResultStatus.NotFound:
                return NotFound();
            case WorkspaceMembershipResultStatus.Forbidden:
                return StatusCode(StatusCodes.Status403Forbidden);
            case WorkspaceMembershipResultStatus.Conflict:
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: "The membership operation conflicts with " +
                        "the current workspace state.");
            default:
                throw new InvalidOperationException(
                    $"Unsupported membership result: {result.Status}");
        }
    }

    private Guid UserId() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static WorkspaceMemberResponseDto ToDto(
        WorkspaceMemberSummary member) => new(
            member.Id,
            member.Email,
            member.Role,
            member.JoinedAt);
}
