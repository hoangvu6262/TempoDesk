using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.WorkItems;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.WorkItems.Persistence;

namespace TempoDesk.Infrastructure.WorkItems;

internal sealed class TaskService(
    TempoDeskIdentityDbContext context,
    IWorkspaceAuthorizationService authorization,
    TimeProvider timeProvider) : ITaskService
{
    public async Task<WorkItemResult<IReadOnlyList<TaskSummary>>> ListAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.ViewWorkItems,
            cancellationToken);
        if (access is not null)
            return ConvertFailure<IReadOnlyList<TaskSummary>>(access);

        if (!await ActiveProjectExistsAsync(
            workspaceId, projectId, cancellationToken))
        {
            return new(WorkItemResultStatus.NotFound);
        }

        var tasks = await ActiveTasks(workspaceId)
            .Where(task => task.ProjectId == projectId)
            .Include(task => task.Assignees)
            .ThenInclude(link => link.User)
            .Include(task => task.TaskLabels)
            .ThenInclude(link => link.Label)
            .AsNoTracking()
            .OrderBy(task => task.ParentTaskId)
            .ThenBy(task => task.SortKey)
            .ThenBy(task => task.Id)
            .ToListAsync(cancellationToken);

        return new(
            WorkItemResultStatus.Success,
            tasks.Select(ToSummary).ToList());
    }

    public async Task<WorkItemResult<TaskSummary>> CreateAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid projectId,
        TaskMutation task,
        CancellationToken cancellationToken = default)
    {
        var errors = WorkItemValidation.Task(task);
        if (errors.Count > 0)
            return Invalid<TaskSummary>(errors);

        var access = await AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.ManageTasks,
            cancellationToken);
        if (access is not null)
            return ConvertFailure<TaskSummary>(access);

        if (!await ActiveProjectExistsAsync(
            workspaceId, projectId, cancellationToken))
        {
            return new(WorkItemResultStatus.NotFound);
        }

        if (task.ParentTaskId is not null &&
            !await IsValidParentAsync(
                workspaceId, projectId, task.ParentTaskId.Value, null,
                cancellationToken))
        {
            return Invalid<TaskSummary>("parentTaskId",
                "Parent task must be an active top-level task in the project.");
        }

        var sortKey = await context.Tasks
            .Where(item => item.WorkspaceId == workspaceId &&
                item.ProjectId == projectId &&
                item.ParentTaskId == task.ParentTaskId)
            .Select(item => (decimal?)item.SortKey)
            .MaxAsync(cancellationToken) ?? 0m;
        var now = timeProvider.GetUtcNow();
        var entity = new WorkTask
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ProjectId = projectId,
            ParentTaskId = task.ParentTaskId,
            CreatedByUserId = actorUserId,
            Title = task.Title!.Trim(),
            Description = WorkItemValidation.NormalizeOptional(task.Description),
            Priority = task.Priority!,
            Status = task.Status!,
            StartDate = task.StartDate,
            DueDate = task.DueDate,
            EstimatedMinutes = task.EstimatedMinutes,
            SourceType = WorkItemSourceTypeValues.Manual,
            SortKey = sortKey + 1000m,
            CreatedAt = now,
            UpdatedAt = now
        };
        entity.ActivityLogs.Add(Activity(
            entity, actorUserId, TaskActivityTypes.Created,
            new { status = entity.Status }, now));

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        context.Tasks.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new(WorkItemResultStatus.Success, ToSummary(entity));
    }

    public async Task<WorkItemResult<TaskSummary>> GetAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.ViewWorkItems,
            cancellationToken);
        if (access is not null)
            return ConvertFailure<TaskSummary>(access);

        var task = await TaskWithDetails(workspaceId)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == taskId,
                cancellationToken);
        return task is null
            ? new(WorkItemResultStatus.NotFound)
            : new(WorkItemResultStatus.Success, ToSummary(task));
    }

    public async Task<WorkItemResult<TaskSummary>> UpdateAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid taskId,
        TaskPatch patch,
        long rowVersion,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.ManageTasks,
            cancellationToken);
        if (access is not null)
            return ConvertFailure<TaskSummary>(access);

        var task = await TaskWithDetails(workspaceId)
            .SingleOrDefaultAsync(item => item.Id == taskId,
                cancellationToken);
        if (task is null)
            return new(WorkItemResultStatus.NotFound);

        var next = new TaskMutation(
            patch.Title.IsSpecified ? patch.Title.Value : task.Title,
            patch.Description.IsSpecified ? patch.Description.Value :
                task.Description,
            patch.Priority.IsSpecified ? patch.Priority.Value : task.Priority,
            patch.Status.IsSpecified ? patch.Status.Value : task.Status,
            patch.StartDate.IsSpecified ? patch.StartDate.Value : task.StartDate,
            patch.DueDate.IsSpecified ? patch.DueDate.Value : task.DueDate,
            patch.EstimatedMinutes.IsSpecified ? patch.EstimatedMinutes.Value :
                task.EstimatedMinutes,
            patch.ParentTaskId.IsSpecified ? patch.ParentTaskId.Value :
                task.ParentTaskId);
        var errors = WorkItemValidation.Task(next);
        if (errors.Count > 0)
            return Invalid<TaskSummary>(errors);

        if (next.ParentTaskId is not null &&
            !await IsValidParentAsync(
                workspaceId, task.ProjectId, next.ParentTaskId.Value, task.Id,
                cancellationToken))
        {
            return Invalid<TaskSummary>("parentTaskId",
                "Parent task must be an active top-level task in the project.");
        }

        var changes = Apply(task, next);
        if (changes.Count == 0)
            return new(WorkItemResultStatus.Success, ToSummary(task));

        context.Entry(task).Property(item => item.RowVersion)
            .OriginalValue = rowVersion;
        context.TaskActivityLogs.Add(Activity(
            task, actorUserId, TaskActivityTypes.Updated,
            new { changes }, timeProvider.GetUtcNow()));

        return await SaveMutationAsync(
            task, workspaceId, cancellationToken);
    }

    public async Task<WorkItemResult<TaskSummary>> DeleteAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid taskId,
        long rowVersion,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.ManageTasks,
            cancellationToken);
        if (access is not null)
            return ConvertFailure<TaskSummary>(access);

        var task = await ActiveTasks(workspaceId)
            .Include(item => item.Assignees)
            .ThenInclude(link => link.User)
            .Include(item => item.TaskLabels)
            .ThenInclude(link => link.Label)
            .SingleOrDefaultAsync(item => item.Id == taskId,
                cancellationToken);
        if (task is null)
            return new(WorkItemResultStatus.NotFound);

        var activeChildren = await ActiveTasks(workspaceId)
            .Where(child => child.ParentTaskId == task.Id)
            .ToListAsync(cancellationToken);

        context.Entry(task).Property(item => item.RowVersion)
            .OriginalValue = rowVersion;
        var now = timeProvider.GetUtcNow();
        foreach (var affected in activeChildren.Prepend(task))
        {
            affected.DeletedAt = now;
            context.TaskActivityLogs.Add(Activity(
                affected, actorUserId, TaskActivityTypes.Deleted,
                new { cascadeRootTaskId = task.Id }, now));
        }

        return await SaveMutationAsync(
            task, workspaceId, cancellationToken);
    }

    public async Task<WorkItemResult<TaskSummary>> RestoreAsync(
        Guid actorUserId,
        Guid workspaceId,
        Guid taskId,
        long rowVersion,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(
            actorUserId, workspaceId, WorkspaceAction.ManageTasks,
            cancellationToken);
        if (access is not null)
            return ConvertFailure<TaskSummary>(access);

        var task = await context.Tasks.IgnoreQueryFilters()
            .Where(item => item.WorkspaceId == workspaceId &&
                item.Project.ArchivedAt == null)
            .Include(item => item.Children)
            .ThenInclude(child => child.ActivityLogs)
            .Include(item => item.Assignees)
            .ThenInclude(link => link.User)
            .Include(item => item.TaskLabels)
            .ThenInclude(link => link.Label)
            .SingleOrDefaultAsync(item => item.Id == taskId,
                cancellationToken);
        if (task?.DeletedAt is null)
            return new(WorkItemResultStatus.NotFound);

        context.Entry(task).Property(item => item.RowVersion)
            .OriginalValue = rowVersion;
        var deletedAt = task.DeletedAt;
        var now = timeProvider.GetUtcNow();
        foreach (var affected in task.Children
            .Where(child => child.DeletedAt == deletedAt &&
                WasCascadeDeletedBy(child, task.Id))
            .Prepend(task))
        {
            affected.DeletedAt = null;
            context.TaskActivityLogs.Add(Activity(
                affected, actorUserId, TaskActivityTypes.Restored,
                new { }, now));
        }

        return await SaveMutationAsync(
            task, workspaceId, cancellationToken);
    }

    public Task<WorkItemResult<TaskSummary>> ReplaceAssigneesAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<WorkItemResult<TaskSummary>> ReplaceLabelsAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId,
        IReadOnlyCollection<string?> names,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<WorkItemResult<IReadOnlyList<TaskCommentSummary>>>
        ListCommentsAsync(
            Guid actorUserId, Guid workspaceId, Guid taskId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<WorkItemResult<TaskCommentSummary>> AddCommentAsync(
        Guid actorUserId, Guid workspaceId, Guid taskId, string? body,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<WorkItemResult<IReadOnlyList<TaskActivitySummary>>>
        ListActivityAsync(
            Guid actorUserId, Guid workspaceId, Guid taskId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private async Task<WorkItemResult<TaskSummary>> SaveMutationAsync(
        WorkTask task,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(WorkItemResultStatus.Success, ToSummary(task));
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            var currentVersion = await ActiveTasks(workspaceId)
                .Where(item => item.Id == task.Id)
                .Select(item => (long?)item.RowVersion)
                .SingleOrDefaultAsync(cancellationToken);
            return currentVersion is null
                ? new(WorkItemResultStatus.NotFound)
                : new(
                    WorkItemResultStatus.Conflict,
                    CurrentRowVersion: currentVersion);
        }
    }

    private async Task<WorkItemResult<object>?> AuthorizeAsync(
        Guid actorUserId,
        Guid workspaceId,
        WorkspaceAction action,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(
            actorUserId, workspaceId, action, cancellationToken);
        return access.Status switch
        {
            WorkspaceAuthorizationStatus.Allowed => null,
            WorkspaceAuthorizationStatus.NotFound =>
                new(WorkItemResultStatus.NotFound),
            WorkspaceAuthorizationStatus.Forbidden =>
                new(WorkItemResultStatus.Forbidden),
            _ => throw new InvalidOperationException(
                "Unsupported authorization result.")
        };
    }

    private static WorkItemResult<T> ConvertFailure<T>(
        WorkItemResult<object> failure) => new(failure.Status);

    private Task<bool> ActiveProjectExistsAsync(
        Guid workspaceId,
        Guid projectId,
        CancellationToken cancellationToken) => context.Projects.AnyAsync(
        project => project.Id == projectId &&
            project.WorkspaceId == workspaceId &&
            project.ArchivedAt == null,
        cancellationToken);

    private Task<bool> IsValidParentAsync(
        Guid workspaceId,
        Guid projectId,
        Guid parentTaskId,
        Guid? currentTaskId,
        CancellationToken cancellationToken) => ActiveTasks(workspaceId)
        .AnyAsync(parent => parent.Id == parentTaskId &&
            parent.ProjectId == projectId &&
            parent.ParentTaskId == null &&
            parent.Id != currentTaskId,
            cancellationToken);

    private IQueryable<WorkTask> ActiveTasks(Guid workspaceId) =>
        context.Tasks.Where(task =>
            task.WorkspaceId == workspaceId &&
            task.Project.ArchivedAt == null);

    private IQueryable<WorkTask> TaskWithDetails(Guid workspaceId) =>
        ActiveTasks(workspaceId)
            .Include(task => task.Assignees)
            .ThenInclude(link => link.User)
            .Include(task => task.TaskLabels)
            .ThenInclude(link => link.Label);

    private static Dictionary<string, object?> Apply(
        WorkTask task,
        TaskMutation next)
    {
        var changes = new Dictionary<string, object?>();
        Change(changes, "title", task.Title, next.Title!.Trim(),
            value => task.Title = value);
        Change(changes, "description", task.Description,
            WorkItemValidation.NormalizeOptional(next.Description),
            value => task.Description = value);
        Change(changes, "priority", task.Priority, next.Priority!,
            value => task.Priority = value);
        Change(changes, "status", task.Status, next.Status!,
            value => task.Status = value);
        Change(changes, "startDate", task.StartDate, next.StartDate,
            value => task.StartDate = value);
        Change(changes, "dueDate", task.DueDate, next.DueDate,
            value => task.DueDate = value);
        Change(changes, "estimatedMinutes", task.EstimatedMinutes,
            next.EstimatedMinutes, value => task.EstimatedMinutes = value);
        Change(changes, "parentTaskId", task.ParentTaskId, next.ParentTaskId,
            value => task.ParentTaskId = value);
        return changes;
    }

    private static void Change<T>(
        Dictionary<string, object?> changes,
        string field,
        T before,
        T after,
        Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
            return;

        changes[field] = new { from = before, to = after };
        apply(after);
    }

    private static TaskActivityLog Activity(
        WorkTask task,
        Guid actorUserId,
        string eventType,
        object details,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = task.WorkspaceId,
            TaskId = task.Id,
            ActorUserId = actorUserId,
            EventType = eventType,
            Details = JsonSerializer.SerializeToDocument(details),
            CreatedAt = now
        };

    private static bool WasCascadeDeletedBy(
        WorkTask task,
        Guid cascadeRootTaskId)
    {
        var deletion = task.ActivityLogs
            .Where(activity => activity.EventType == TaskActivityTypes.Deleted)
            .OrderByDescending(activity => activity.CreatedAt)
            .ThenByDescending(activity => activity.Id)
            .FirstOrDefault();
        return deletion is not null &&
            deletion.Details.RootElement.TryGetProperty(
                "cascadeRootTaskId", out var root) &&
            root.ValueKind == JsonValueKind.String &&
            root.TryGetGuid(out var rootId) &&
            rootId == cascadeRootTaskId;
    }

    private static TaskSummary ToSummary(WorkTask task) => new(
        task.Id,
        task.WorkspaceId,
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
        task.Assignees
            .OrderBy(link => link.User.Email)
            .Select(link => new AssigneeSummary(
                link.UserId, link.User.Email!))
            .ToList(),
        task.TaskLabels
            .OrderBy(link => link.Label.Name)
            .Select(link => new LabelSummary(
                link.LabelId, link.Label.Name))
            .ToList());

    private static WorkItemResult<T> Invalid<T>(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(WorkItemResultStatus.Invalid, Errors: errors);

    private static WorkItemResult<T> Invalid<T>(
        string field,
        string error) => Invalid<T>(
        new Dictionary<string, string[]> { [field] = [error] });
}
