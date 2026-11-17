using System.Text.Json;

namespace TempoDesk.Api.Controllers;

public sealed record CreateProjectRequestDto(
    string? Name,
    string? Description);

public sealed record UpdateProjectRequestDto(
    JsonElement? Name,
    JsonElement? Description);

public sealed record CreateTaskRequestDto(
    string? Title,
    string? Description,
    string? Priority,
    string? Status,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int? EstimatedMinutes,
    Guid? ParentTaskId);

public sealed record UpdateTaskRequestDto(
    JsonElement? Title = null,
    JsonElement? Description = null,
    JsonElement? Priority = null,
    JsonElement? Status = null,
    JsonElement? StartDate = null,
    JsonElement? DueDate = null,
    JsonElement? EstimatedMinutes = null,
    JsonElement? ParentTaskId = null);

public sealed record ReplaceTaskAssigneesRequestDto(
    IReadOnlyList<Guid>? UserIds);

public sealed record ReplaceTaskLabelsRequestDto(
    IReadOnlyList<string?>? Names);

public sealed record AddTaskCommentRequestDto(string? Body);
