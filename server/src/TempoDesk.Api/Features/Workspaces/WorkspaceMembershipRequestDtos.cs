namespace TempoDesk.Api.Controllers;

public sealed record InviteWorkspaceMemberRequestDto(
    string? Email,
    string? Role);

public sealed record AcceptWorkspaceInvitationRequestDto(string? Token);

public sealed record ChangeWorkspaceMemberRoleRequestDto(string? Role);

public sealed record TransferWorkspaceOwnershipRequestDto(Guid MemberId);
