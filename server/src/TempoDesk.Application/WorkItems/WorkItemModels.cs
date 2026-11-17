using System.Text.Json;

namespace TempoDesk.Application.WorkItems;

public readonly record struct FieldUpdate<T>(bool IsSpecified, T? Value);

public enum WorkItemResultStatus
{
    Success,
    Invalid,
    NotFound,
    Forbidden,
    Conflict
}

public sealed record WorkItemResult<T>(
    WorkItemResultStatus Status,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    long? CurrentRowVersion = null);

public sealed record ProjectSummary(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AssigneeSummary(Guid UserId, string Email);

public sealed record LabelSummary(Guid Id, string Name);

public sealed record TaskSummary(
    Guid Id,
    Guid WorkspaceId,
    Guid ProjectId,
    Guid? ParentTaskId,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int? EstimatedMinutes,
    string SourceType,
    decimal SortKey,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AssigneeSummary> Assignees,
    IReadOnlyList<LabelSummary> Labels);

public sealed record TaskMutation(
    string? Title,
    string? Description,
    string? Priority,
    string? Status,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int? EstimatedMinutes,
    Guid? ParentTaskId);

public sealed record TaskPatch(
    FieldUpdate<string> Title,
    FieldUpdate<string?> Description,
    FieldUpdate<string> Priority,
    FieldUpdate<string> Status,
    FieldUpdate<DateOnly?> StartDate,
    FieldUpdate<DateOnly?> DueDate,
    FieldUpdate<int?> EstimatedMinutes,
    FieldUpdate<Guid?> ParentTaskId);

public sealed record TaskCommentSummary(
    Guid Id,
    Guid AuthorUserId,
    string AuthorEmail,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record TaskActivitySummary(
    Guid Id,
    Guid ActorUserId,
    string ActorEmail,
    string EventType,
    JsonElement Details,
    DateTimeOffset CreatedAt);

public sealed record MyWorkSummary(
    IReadOnlyList<TaskSummary> RecentlyCompleted,
    IReadOnlyList<TaskSummary> Overdue,
    IReadOnlyList<TaskSummary> Today,
    IReadOnlyList<TaskSummary> Upcoming,
    IReadOnlyList<TaskSummary> Later,
    IReadOnlyList<TaskSummary> Unscheduled);

public sealed record TextValidationResult(
    string? Value,
    string? NormalizedValue,
    IReadOnlyDictionary<string, string[]> Errors);
