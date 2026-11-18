using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.WorkItems;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces/{workspaceId:guid}/tasks/{taskId:guid}")]
public sealed class TaskDetailsController(
    ITaskService tasks) : ControllerBase
{
    [HttpPut("assignees")]
    public async Task<IActionResult> ReplaceAssignees(
        Guid workspaceId,
        Guid taskId,
        ReplaceTaskAssigneesRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await tasks.ReplaceAssigneesAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            taskId,
            request.UserIds ?? [],
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? WorkItemControllerSupport.TaskSuccess(this, result.Value!)
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpPut("labels")]
    public async Task<IActionResult> ReplaceLabels(
        Guid workspaceId,
        Guid taskId,
        ReplaceTaskLabelsRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await tasks.ReplaceLabelsAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            taskId,
            request.Names ?? [],
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? WorkItemControllerSupport.TaskSuccess(this, result.Value!)
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpGet("comments")]
    public async Task<IActionResult> ListComments(
        Guid workspaceId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var result = await tasks.ListCommentsAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            taskId,
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? Ok(result.Value!.Select(WorkItemControllerSupport.ToDto))
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpPost("comments")]
    public async Task<IActionResult> AddComment(
        Guid workspaceId,
        Guid taskId,
        AddTaskCommentRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await tasks.AddCommentAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            taskId,
            request.Body,
            cancellationToken);
        if (result.Status != WorkItemResultStatus.Success)
            return WorkItemControllerSupport.Failure(this, result);

        var response = WorkItemControllerSupport.ToDto(result.Value!);
        return CreatedAtAction(
            nameof(ListComments),
            new { workspaceId, taskId },
            response);
    }

    [HttpGet("activity")]
    public async Task<IActionResult> ListActivity(
        Guid workspaceId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var result = await tasks.ListActivityAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            taskId,
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? Ok(result.Value!.Select(WorkItemControllerSupport.ToDto))
            : WorkItemControllerSupport.Failure(this, result);
    }
}
