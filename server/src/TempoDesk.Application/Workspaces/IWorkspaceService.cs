namespace TempoDesk.Application.Workspaces;

public interface IWorkspaceService
{
    Task<IReadOnlyList<WorkspaceSummary>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceResult> CreateTeamAsync(
        Guid userId,
        string? name,
        CancellationToken cancellationToken = default);

    Task<WorkspaceResult> GetAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceResult> RenameAsync(
        Guid userId,
        Guid workspaceId,
        string? name,
        CancellationToken cancellationToken = default);
}
