using System.Security.Claims;
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
