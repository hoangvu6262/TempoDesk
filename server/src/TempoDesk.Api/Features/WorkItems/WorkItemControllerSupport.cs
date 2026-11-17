using System.Security.Claims;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.WorkItems;

namespace TempoDesk.Api.Controllers;

internal static class WorkItemControllerSupport
{
    public static Guid UserId(ControllerBase controller) => Guid.Parse(
        controller.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static ProjectResponseDto ToDto(ProjectSummary project) => new(
        project.Id,
        project.Name,
        project.Description,
        project.CreatedAt,
        project.UpdatedAt);

    public static TaskResponseDto ToDto(TaskSummary task) => new(
        task.Id,
        task.ProjectId,
        task.ParentTaskId,
        task.Title,
        task.Description,
        task.Priority,
        task.Status,
        task.StartDate,
        task.DueDate,
        task.EstimatedMinutes,
        task.SourceType,
        task.SortKey,
        task.RowVersion,
        task.CreatedAt,
        task.UpdatedAt,
        task.Assignees.Select(item => new AssigneeResponseDto(
            item.UserId, item.Email)).ToList(),
        task.Labels.Select(item => new LabelResponseDto(
            item.Id, item.Name)).ToList());

    public static TaskCommentResponseDto ToDto(TaskCommentSummary comment) =>
        new(
            comment.Id,
            comment.AuthorUserId,
            comment.AuthorEmail,
            comment.Body,
            comment.CreatedAt);

    public static TaskActivityResponseDto ToDto(
        TaskActivitySummary activity) => new(
        activity.Id,
        activity.ActorUserId,
        activity.ActorEmail,
        activity.EventType,
        activity.Details,
        activity.CreatedAt);

    public static IActionResult TaskSuccess(
        ControllerBase controller,
        TaskSummary task)
    {
        controller.Response.Headers.ETag = $"\"{task.RowVersion}\"";
        return controller.Ok(ToDto(task));
    }

    public static bool TryParseIfMatch(
        ControllerBase controller,
        out long rowVersion)
    {
        var values = controller.Request.Headers.IfMatch;
        rowVersion = 0;
        if (values.Count != 1)
        {
            return false;
        }

        var value = values[0];
        return value is not null &&
            value.Length > 2 &&
            value[0] == '"' &&
            value[^1] == '"' &&
            long.TryParse(
                value.AsSpan(1, value.Length - 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out rowVersion) &&
            rowVersion > 0;
    }

    public static IActionResult Failure<T>(
        ControllerBase controller,
        WorkItemResult<T> result) => result.Status switch
        {
            WorkItemResultStatus.Invalid => Validation(
                controller, result.Errors!),
            WorkItemResultStatus.NotFound => controller.NotFound(),
            WorkItemResultStatus.Forbidden => controller.StatusCode(
                StatusCodes.Status403Forbidden),
            WorkItemResultStatus.Conflict => controller.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                extensions: result.CurrentRowVersion is null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["currentRowVersion"] = result.CurrentRowVersion
                    }),
            _ => throw new InvalidOperationException(
                $"Unsupported work item result: {result.Status}")
        };

    public static bool TryRequiredString(
        JsonElement? element,
        out FieldUpdate<string> update,
        out string? error)
    {
        if (element is null)
        {
            update = default;
            error = null;
            return true;
        }

        if (element.Value.ValueKind != JsonValueKind.String)
        {
            update = default;
            error = "Value must be a string.";
            return false;
        }

        update = new(true, element.Value.GetString());
        error = null;
        return true;
    }

    public static bool TryNullableString(
        JsonElement? element,
        out FieldUpdate<string?> update,
        out string? error)
    {
        if (element is null)
        {
            update = default;
            error = null;
            return true;
        }

        if (element.Value.ValueKind == JsonValueKind.Null)
        {
            update = new(true, null);
            error = null;
            return true;
        }

        if (element.Value.ValueKind != JsonValueKind.String)
        {
            update = default;
            error = "Value must be a string or null.";
            return false;
        }

        update = new(true, element.Value.GetString());
        error = null;
        return true;
    }

    public static bool TryNullableGuid(
        JsonElement? element,
        out FieldUpdate<Guid?> update,
        out string? error)
    {
        if (element is null)
        {
            update = default;
            error = null;
            return true;
        }
        if (element.Value.ValueKind == JsonValueKind.Null)
        {
            update = new(true, null);
            error = null;
            return true;
        }
        if (element.Value.ValueKind == JsonValueKind.String &&
            element.Value.TryGetGuid(out var value))
        {
            update = new(true, value);
            error = null;
            return true;
        }
        update = default;
        error = "Value must be a UUID or null.";
        return false;
    }

    public static bool TryNullableDate(
        JsonElement? element,
        out FieldUpdate<DateOnly?> update,
        out string? error)
    {
        if (element is null)
        {
            update = default;
            error = null;
            return true;
        }
        if (element.Value.ValueKind == JsonValueKind.Null)
        {
            update = new(true, null);
            error = null;
            return true;
        }
        if (element.Value.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(
                element.Value.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var value))
        {
            update = new(true, value);
            error = null;
            return true;
        }
        update = default;
        error = "Value must be a yyyy-MM-dd date or null.";
        return false;
    }

    public static bool TryNullableInt(
        JsonElement? element,
        out FieldUpdate<int?> update,
        out string? error)
    {
        if (element is null)
        {
            update = default;
            error = null;
            return true;
        }
        if (element.Value.ValueKind == JsonValueKind.Null)
        {
            update = new(true, null);
            error = null;
            return true;
        }
        if (element.Value.ValueKind == JsonValueKind.Number &&
            element.Value.TryGetInt32(out var value))
        {
            update = new(true, value);
            error = null;
            return true;
        }
        update = default;
        error = "Value must be an integer or null.";
        return false;
    }

    public static IActionResult Validation(
        ControllerBase controller,
        IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
                controller.ModelState.AddModelError(field, message);
        }

        return controller.ValidationProblem(controller.ModelState);
    }
}
