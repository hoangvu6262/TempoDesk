using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.WorkItems;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces/{workspaceId:guid}/projects")]
public sealed class ProjectsController(
    IProjectService projects) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await projects.ListAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? Ok(result.Value!.Select(WorkItemControllerSupport.ToDto))
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid workspaceId,
        CreateProjectRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await projects.CreateAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            request.Name,
            request.Description,
            cancellationToken);
        if (result.Status != WorkItemResultStatus.Success)
            return WorkItemControllerSupport.Failure(this, result);

        var response = WorkItemControllerSupport.ToDto(result.Value!);
        return CreatedAtAction(
            nameof(Read),
            new { workspaceId, projectId = response.Id },
            response);
    }

    [HttpGet("{projectId:guid}")]
    public async Task<IActionResult> Read(
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var result = await projects.GetAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            projectId,
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? Ok(WorkItemControllerSupport.ToDto(result.Value!))
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpPatch("{projectId:guid}")]
    public async Task<IActionResult> Update(
        Guid workspaceId,
        Guid projectId,
        UpdateProjectRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!WorkItemControllerSupport.TryRequiredString(
            request.Name, out var name, out var nameError))
        {
            return WorkItemControllerSupport.Validation(this,
                new Dictionary<string, string[]>
                {
                    ["name"] = [nameError!]
                });
        }

        if (!WorkItemControllerSupport.TryNullableString(
            request.Description,
            out var description,
            out var descriptionError))
        {
            return WorkItemControllerSupport.Validation(this,
                new Dictionary<string, string[]>
                {
                    ["description"] = [descriptionError!]
                });
        }

        var result = await projects.UpdateAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            projectId,
            name,
            description,
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? Ok(WorkItemControllerSupport.ToDto(result.Value!))
            : WorkItemControllerSupport.Failure(this, result);
    }

    [HttpDelete("{projectId:guid}")]
    public async Task<IActionResult> Archive(
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var result = await projects.ArchiveAsync(
            WorkItemControllerSupport.UserId(this),
            workspaceId,
            projectId,
            cancellationToken);
        return result.Status == WorkItemResultStatus.Success
            ? Ok(WorkItemControllerSupport.ToDto(result.Value!))
            : WorkItemControllerSupport.Failure(this, result);
    }
}
