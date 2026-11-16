using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Workspaces;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces/{workspaceId:guid}")]
public sealed class WorkspaceMembershipController(
    IWorkspaceMembershipService memberships) : ControllerBase
{
    [HttpGet("members")]
    public async Task<IActionResult> ListMembers(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await memberships.ListMembersAsync(
            UserId(), workspaceId, cancellationToken);
        return result.Status == WorkspaceMembershipResultStatus.Success
            ? Ok(result.Members!.Select(ToDto))
            : Failure(result);
    }

    [HttpGet("invitations")]
    public async Task<IActionResult> ListInvitations(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await memberships.ListInvitationsAsync(
            UserId(), workspaceId, cancellationToken);
        return result.Status == WorkspaceMembershipResultStatus.Success
            ? Ok(result.Invitations!.Select(ToDto))
            : Failure(result);
    }

    [HttpPost("invitations")]
    public async Task<IActionResult> Invite(
        Guid workspaceId,
        InviteWorkspaceMemberRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await memberships.InviteAsync(
            UserId(), workspaceId, request.Email, request.Role,
            cancellationToken);
        if (result.Status != WorkspaceMembershipResultStatus.Success)
            return Failure(result);

        return CreatedAtAction(
            nameof(ListInvitations),
            new { workspaceId },
            ToDto(result.Invitation!));
    }

    [HttpDelete("invitations/{invitationId:guid}")]
    public async Task<IActionResult> CancelInvitation(
        Guid workspaceId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var result = await memberships.CancelInvitationAsync(
            UserId(), workspaceId, invitationId, cancellationToken);
        return result.Status == WorkspaceMembershipResultStatus.Success
            ? NoContent()
            : Failure(result);
    }

    [HttpPatch("members/{memberId:guid}")]
    public async Task<IActionResult> ChangeRole(
        Guid workspaceId,
        Guid memberId,
        ChangeWorkspaceMemberRoleRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await memberships.ChangeRoleAsync(
            UserId(), workspaceId, memberId, request.Role,
            cancellationToken);
        return result.Status == WorkspaceMembershipResultStatus.Success
            ? Ok(ToDto(result.Member!))
            : Failure(result);
    }

    [HttpDelete("members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid workspaceId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var result = await memberships.RemoveMemberAsync(
            UserId(), workspaceId, memberId, cancellationToken);
        return result.Status == WorkspaceMembershipResultStatus.Success
            ? NoContent()
            : Failure(result);
    }

    [HttpPost("leave")]
    public async Task<IActionResult> Leave(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await memberships.LeaveAsync(
            UserId(), workspaceId, cancellationToken);
        return result.Status == WorkspaceMembershipResultStatus.Success
            ? NoContent()
            : Failure(result);
    }

    [HttpPost("transfer-ownership")]
    public async Task<IActionResult> TransferOwnership(
        Guid workspaceId,
        TransferWorkspaceOwnershipRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await memberships.TransferOwnershipAsync(
            UserId(), workspaceId, request.MemberId, cancellationToken);
        return result.Status == WorkspaceMembershipResultStatus.Success
            ? NoContent()
            : Failure(result);
    }

    private IActionResult Failure(WorkspaceMembershipResult result)
    {
        switch (result.Status)
        {
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

    private static WorkspaceInvitationResponseDto ToDto(
        WorkspaceInvitationSummary invitation) => new(
            invitation.Id,
            invitation.Email,
            invitation.Role,
            invitation.CreatedAt,
            invitation.ExpiresAt);
}
