namespace TempoDesk.Api.Controllers;

public sealed record ProjectResponseDto(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AssigneeResponseDto(Guid UserId, string Email);

public sealed record LabelResponseDto(Guid Id, string Name);

public sealed record TaskResponseDto(
    Guid Id,
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
    IReadOnlyList<AssigneeResponseDto> Assignees,
    IReadOnlyList<LabelResponseDto> Labels);

public sealed record TaskCommentResponseDto(
    Guid Id,
    Guid AuthorUserId,
    string AuthorEmail,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record TaskActivityResponseDto(
    Guid Id,
    Guid ActorUserId,
    string ActorEmail,
    string EventType,
    System.Text.Json.JsonElement Details,
    DateTimeOffset CreatedAt);
