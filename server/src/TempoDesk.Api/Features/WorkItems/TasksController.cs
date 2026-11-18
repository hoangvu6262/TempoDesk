using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.WorkItems;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces/{workspaceId:guid}")]
public sealed class TasksController(
    ITaskService tasks) : ControllerBase
{
    [HttpGet("projects/{projectId:guid}/tasks")]
    public async Task<IActionResult> List(
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var result = await tasks.ListAsync(
            WorkItemControllerSupport.UserId(this), workspaceId, projectId,
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? Ok(result.Value!.Select(WorkItemControllerSupport.ToDto))
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpPost("projects/{projectId:guid}/tasks")]
    public async Task<IActionResult> Create(
        Guid workspaceId,
        Guid projectId,
        CreateTaskRequestDto request,
        CancellationToken cancellationToken)
    {
        var mutation = new TaskMutation(
            request.Title,
            request.Description,
            request.Priority ?? WorkItemPriorityValues.None,
            request.Status ?? WorkItemStatusValues.Todo,
            request.StartDate,
            request.DueDate,
            request.EstimatedMinutes,
            request.ParentTaskId);
        var result = await tasks.CreateAsync(
            WorkItemControllerSupport.UserId(this), workspaceId, projectId,
            mutation, cancellationToken);
        if (result.Status != WorkItemResultStatus.Success)
            return WorkItemControllerSupport.Failure(this, result);

        Response.Headers.ETag = $"\"{result.Value!.RowVersion}\"";
        return CreatedAtAction(
            nameof(Read),
            new { workspaceId, taskId = result.Value.Id },
            WorkItemControllerSupport.ToDto(result.Value));
    }

    [HttpGet("tasks/{taskId:guid}")]
    public async Task<IActionResult> Read(
        Guid workspaceId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var result = await tasks.GetAsync(
            WorkItemControllerSupport.UserId(this), workspaceId, taskId,
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? WorkItemControllerSupport.TaskSuccess(this, result.Value!)
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpPatch("tasks/{taskId:guid}")]
    public async Task<IActionResult> Update(
        Guid workspaceId,
        Guid taskId,
        UpdateTaskRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryPrecondition(out var rowVersion))
        {
            return WorkItemControllerSupport.Validation(this,
                PreconditionErrors());
        }
        if (!TryPatch(request, out var patch, out var errors))
        {
            return WorkItemControllerSupport.Validation(this, errors);
        }

        var result = await tasks.UpdateAsync(
            WorkItemControllerSupport.UserId(this), workspaceId, taskId,
            patch, rowVersion, cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? WorkItemControllerSupport.TaskSuccess(this, result.Value!)
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpDelete("tasks/{taskId:guid}")]
    public async Task<IActionResult> Delete(
        Guid workspaceId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        if (!TryPrecondition(out var rowVersion))
        {
            return WorkItemControllerSupport.Validation(this,
                PreconditionErrors());
        }
        var result = await tasks.DeleteAsync(
            WorkItemControllerSupport.UserId(this), workspaceId, taskId,
            rowVersion, cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? WorkItemControllerSupport.TaskSuccess(this, result.Value!)
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpPost("tasks/{taskId:guid}/restore")]
    public async Task<IActionResult> Restore(
        Guid workspaceId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        if (!TryPrecondition(out var rowVersion))
        {
            return WorkItemControllerSupport.Validation(this,
                PreconditionErrors());
        }
        var result = await tasks.RestoreAsync(
            WorkItemControllerSupport.UserId(this), workspaceId, taskId,
            rowVersion, cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? WorkItemControllerSupport.TaskSuccess(this, result.Value!)
            : WorkItemControllerSupport.Failure(this, result);
    }

    private bool TryPrecondition(out long rowVersion) =>
        WorkItemControllerSupport.TryParseIfMatch(this, out rowVersion);

    private static Dictionary<string, string[]> PreconditionErrors() => new()
    {
        ["ifMatch"] = ["If-Match must contain one quoted positive row version."]
    };

    private static bool TryPatch(
        UpdateTaskRequestDto request,
        out TaskPatch patch,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var found = new Dictionary<string, string[]>();
        Read(WorkItemControllerSupport.TryRequiredString(
            request.Title, out var title, out var titleError),
            "title", titleError, found);
        Read(WorkItemControllerSupport.TryNullableString(
            request.Description, out var description, out var descriptionError),
            "description", descriptionError, found);
        Read(WorkItemControllerSupport.TryRequiredString(
            request.Priority, out var priority, out var priorityError),
            "priority", priorityError, found);
        Read(WorkItemControllerSupport.TryRequiredString(
            request.Status, out var status, out var statusError),
            "status", statusError, found);
        Read(WorkItemControllerSupport.TryNullableDate(
            request.StartDate, out var startDate, out var startDateError),
            "startDate", startDateError, found);
        Read(WorkItemControllerSupport.TryNullableDate(
            request.DueDate, out var dueDate, out var dueDateError),
            "dueDate", dueDateError, found);
        Read(WorkItemControllerSupport.TryNullableInt(
            request.EstimatedMinutes,
            out var estimatedMinutes,
            out var estimatedMinutesError),
            "estimatedMinutes", estimatedMinutesError, found);
        Read(WorkItemControllerSupport.TryNullableGuid(
            request.ParentTaskId,
            out var parentTaskId,
            out var parentTaskIdError),
            "parentTaskId", parentTaskIdError, found);

        patch = new(
            title, description, priority, status, startDate, dueDate,
            estimatedMinutes, parentTaskId);
        errors = found;
        return found.Count == 0;
    }

    private static void Read(
        bool valid,
        string field,
        string? error,
        Dictionary<string, string[]> errors)
    {
        if (!valid)
            errors[field] = [error!];
    }
}
