namespace TempoDesk.Application.WorkItems;

public interface ITaskService
{
    Task<WorkItemResult<IReadOnlyList<TaskSummary>>> ListAsync(
        Guid actorUserId, Guid workspaceId, Guid projectId,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<TaskSummary>> CreateAsync(
        Guid actorUserId, Guid workspaceId, Guid projectId,
        TaskMutation task,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<TaskSummary>> GetAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<TaskSummary>> UpdateAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId,
        TaskPatch patch, long rowVersion,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<TaskSummary>> DeleteAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId, long rowVersion,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<TaskSummary>> RestoreAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId, long rowVersion,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<TaskSummary>> ReplaceAssigneesAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<TaskSummary>> ReplaceLabelsAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId,
        IReadOnlyCollection<string?> names,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<IReadOnlyList<TaskCommentSummary>>> ListCommentsAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<TaskCommentSummary>> AddCommentAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId, string? body,
        CancellationToken cancellationToken = default);

    Task<WorkItemResult<IReadOnlyList<TaskActivitySummary>>> ListActivityAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId,
        CancellationToken cancellationToken = default);
}
