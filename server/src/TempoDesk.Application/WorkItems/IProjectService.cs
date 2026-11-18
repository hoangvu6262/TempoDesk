namespace TempoDesk.Application.WorkItems;

public interface IProjectService
{
    Task<WorkItemResult<IReadOnlyList<ProjectSummary>>> ListAsync(
        Guid actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<ProjectSummary>> CreateAsync(
        Guid actorUserId,
        Guid workspaceId,
        string? name,
        string? description,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<ProjectSummary>> GetAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<ProjectSummary>> UpdateAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid projectId,
        FieldUpdate<string> name,
        FieldUpdate<string?> description,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<ProjectSummary>> ArchiveAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken = default);
}
