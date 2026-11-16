namespace TempoDesk.Application.Workspaces;

public interface IWorkspaceAuthorizationService
{
    Task<WorkspaceAuthorizationResult> AuthorizeAsync(
        Guid userId,
        Guid workspaceId,
        WorkspaceAction action,
        CancellationToken cancellationToken = default);
}
