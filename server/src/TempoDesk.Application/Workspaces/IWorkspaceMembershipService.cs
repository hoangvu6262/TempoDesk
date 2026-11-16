namespace TempoDesk.Application.Workspaces;

public interface IWorkspaceMembershipService
{
    Task<WorkspaceMembershipResult> ListMembersAsync(
        Guid actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceMembershipResult> ListInvitationsAsync(
        Guid actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceMembershipResult> InviteAsync(
        Guid actorUserId,
        Guid workspaceId,
        string? email,
        string? role,
        CancellationToken cancellationToken = default);

    Task<WorkspaceMembershipResult> CancelInvitationAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid invitationId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceMembershipResult> AcceptAsync(
        Guid userId,
        string? token,
        CancellationToken cancellationToken = default);

    Task<WorkspaceMembershipResult> ChangeRoleAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid memberId,
        string? role,
        CancellationToken cancellationToken = default);

    Task<WorkspaceMembershipResult> RemoveMemberAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid memberId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceMembershipResult> LeaveAsync(
        Guid actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceMembershipResult> TransferOwnershipAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid targetMemberId,
        CancellationToken cancellationToken = default);
}
