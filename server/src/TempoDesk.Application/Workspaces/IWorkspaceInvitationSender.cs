namespace TempoDesk.Application.Workspaces;

public interface IWorkspaceInvitationSender
{
    Task<bool> SendAsync(
        WorkspaceInvitationEmail message,
        CancellationToken cancellationToken);
}
