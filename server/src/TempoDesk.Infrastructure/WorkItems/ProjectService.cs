using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.WorkItems;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.WorkItems.Persistence;

namespace TempoDesk.Infrastructure.WorkItems;

internal sealed class ProjectService(
    TempoDeskIdentityDbContext context,
    IWorkspaceAuthorizationService authorization,
    TimeProvider timeProvider) : IProjectService
{
    public async Task<WorkItemResult<IReadOnlyList<ProjectSummary>>> ListAsync(
        Guid actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var access = await authorization.AuthorizeAsync(
            actorUserId,
            workspaceId,
            WorkspaceAction.ViewWorkItems,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure<IReadOnlyList<ProjectSummary>>(access);

        var projects = await ActiveProjects(workspaceId)
            .AsNoTracking()
            .OrderBy(project => project.Name)
            .ThenBy(project => project.Id)
            .Select(project => ToSummary(project))
            .ToListAsync(cancellationToken);

        return new(WorkItemResultStatus.Success, projects);
    }

    public async Task<WorkItemResult<ProjectSummary>> CreateAsync(
        Guid actorUserId,
        Guid workspaceId,
        string? name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var validation = WorkItemValidation.ProjectName(name);
        if (validation.Errors.Count > 0)
            return Invalid<ProjectSummary>(validation.Errors);

        var access = await authorization.AuthorizeAsync(
            actorUserId,
            workspaceId,
            WorkspaceAction.ManageProjects,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure<ProjectSummary>(access);

        var now = timeProvider.GetUtcNow();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = validation.Value!,
            Description = WorkItemValidation.NormalizeOptional(description),
            CreatedAt = now,
            UpdatedAt = now
        };
        context.Projects.Add(project);
        await context.SaveChangesAsync(cancellationToken);

        return new(WorkItemResultStatus.Success, ToSummary(project));
    }

    public async Task<WorkItemResult<ProjectSummary>> GetAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var access = await authorization.AuthorizeAsync(
            actorUserId,
            workspaceId,
            WorkspaceAction.ViewWorkItems,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure<ProjectSummary>(access);

        var project = await ActiveProjects(workspaceId)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == projectId,
                cancellationToken);

        return project is null
            ? new(WorkItemResultStatus.NotFound)
            : new(WorkItemResultStatus.Success, ToSummary(project));
    }

    public async Task<WorkItemResult<ProjectSummary>> UpdateAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid projectId,
        FieldUpdate<string> name,
        FieldUpdate<string?> description,
        CancellationToken cancellationToken = default)
    {
        TextValidationResult? nameValidation = null;
        if (name.IsSpecified)
        {
            nameValidation = WorkItemValidation.ProjectName(name.Value);
            if (nameValidation.Errors.Count > 0)
                return Invalid<ProjectSummary>(nameValidation.Errors);
        }

        var access = await authorization.AuthorizeAsync(
            actorUserId,
            workspaceId,
            WorkspaceAction.ManageProjects,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure<ProjectSummary>(access);

        var project = await ActiveProjects(workspaceId)
            .SingleOrDefaultAsync(
                item => item.Id == projectId,
                cancellationToken);
        if (project is null)
            return new(WorkItemResultStatus.NotFound);

        var changed = false;
        if (nameValidation is not null &&
            project.Name != nameValidation.Value)
        {
            project.Name = nameValidation.Value!;
            changed = true;
        }

        if (description.IsSpecified)
        {
            var normalized = WorkItemValidation.NormalizeOptional(
                description.Value);
            if (project.Description != normalized)
            {
                project.Description = normalized;
                changed = true;
            }
        }

        if (changed)
        {
            project.UpdatedAt = timeProvider.GetUtcNow();
            await context.SaveChangesAsync(cancellationToken);
        }

        return new(WorkItemResultStatus.Success, ToSummary(project));
    }

    public async Task<WorkItemResult<ProjectSummary>> ArchiveAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var access = await authorization.AuthorizeAsync(
            actorUserId,
            workspaceId,
            WorkspaceAction.ManageProjects,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure<ProjectSummary>(access);

        var project = await ActiveProjects(workspaceId)
            .SingleOrDefaultAsync(
                item => item.Id == projectId,
                cancellationToken);
        if (project is null)
            return new(WorkItemResultStatus.NotFound);

        var now = timeProvider.GetUtcNow();
        project.ArchivedAt = now;
        project.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);

        return new(WorkItemResultStatus.Success, ToSummary(project));
    }

    private IQueryable<Project> ActiveProjects(Guid workspaceId) =>
        context.Projects.Where(project =>
            project.WorkspaceId == workspaceId &&
            project.ArchivedAt == null);

    private static ProjectSummary ToSummary(Project project) => new(
        project.Id,
        project.WorkspaceId,
        project.Name,
        project.Description,
        project.CreatedAt,
        project.UpdatedAt);

    private static WorkItemResult<T> Invalid<T>(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(WorkItemResultStatus.Invalid, Errors: errors);

    private static WorkItemResult<T> AuthorizationFailure<T>(
        WorkspaceAuthorizationResult access) => access.Status switch
        {
            WorkspaceAuthorizationStatus.NotFound =>
                new(WorkItemResultStatus.NotFound),
            WorkspaceAuthorizationStatus.Forbidden =>
                new(WorkItemResultStatus.Forbidden),
            _ => throw new InvalidOperationException(
                "Allowed authorization is not a failure.")
        };
}
