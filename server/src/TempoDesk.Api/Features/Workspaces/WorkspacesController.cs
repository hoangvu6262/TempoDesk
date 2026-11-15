using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Workspaces;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces")]
public sealed class WorkspacesController(
    IWorkspaceService workspaces) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        CancellationToken cancellationToken)
    {
        var result = await workspaces.ListAsync(
            UserId(),
            cancellationToken);
        return Ok(result.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateWorkspaceRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateName(request.Name, out var normalized);
        if (validation is not null)
            return validation;

        var result = await workspaces.CreateTeamAsync(
            UserId(),
            normalized,
            cancellationToken);
        if (result.Status != WorkspaceResultStatus.Success)
            return Failure(result);

        var response = ToDto(result.Workspace!);
        return CreatedAtAction(
            nameof(Read),
            new { workspaceId = response.Id },
            response);
    }

    [HttpGet("{workspaceId:guid}")]
    public async Task<IActionResult> Read(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await workspaces.GetAsync(
            UserId(),
            workspaceId,
            cancellationToken);
        return result.Status == WorkspaceResultStatus.Success
            ? Ok(ToDto(result.Workspace!))
            : Failure(result);
    }

    [HttpPatch("{workspaceId:guid}")]
    public async Task<IActionResult> Rename(
        Guid workspaceId,
        RenameWorkspaceRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateName(request.Name, out var normalized);
        if (validation is not null)
            return validation;

        var result = await workspaces.RenameAsync(
            UserId(),
            workspaceId,
            normalized,
            cancellationToken);
        return result.Status == WorkspaceResultStatus.Success
            ? Ok(ToDto(result.Workspace!))
            : Failure(result);
    }

    private IActionResult Failure(WorkspaceResult result)
    {
        switch (result.Status)
        {
            case WorkspaceResultStatus.InvalidName:
                ModelState.AddModelError(
                    "name",
                    result.ValidationError!);
                return ValidationProblem(ModelState);
            case WorkspaceResultStatus.NotFound:
                return NotFound();
            case WorkspaceResultStatus.Forbidden:
                return StatusCode(StatusCodes.Status403Forbidden);
            case WorkspaceResultStatus.PersonalNameManaged:
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail:
                        "Personal Workspace names are managed by the system.");
            default:
                throw new InvalidOperationException(
                    $"Unsupported workspace result: {result.Status}");
        }
    }

    private ActionResult? ValidateName(
        string? name,
        out string normalized)
    {
        var value = WorkspaceName.Normalize(name, out var error);
        normalized = value ?? "";
        if (value is not null)
            return null;

        ModelState.AddModelError("name", error!);
        return ValidationProblem(ModelState);
    }

    private Guid UserId() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static WorkspaceResponseDto ToDto(
        WorkspaceSummary workspace) => new(
            workspace.Id,
            workspace.Name,
            workspace.Type,
            workspace.Role);
}
