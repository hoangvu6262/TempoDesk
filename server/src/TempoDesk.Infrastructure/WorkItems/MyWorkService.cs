using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.WorkItems;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.WorkItems.Persistence;
using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.WorkItems;

internal sealed class MyWorkService(
    TempoDeskIdentityDbContext context,
    TimeProvider timeProvider) : IMyWorkService
{
    public async Task<WorkItemResult<MyWorkSummary>> GetAsync(
        Guid actorUserId,
        string? timeZone,
        CancellationToken cancellationToken = default)
    {
        if (!TryTimeZone(timeZone, out var zone))
        {
            return new(
                WorkItemResultStatus.Invalid,
                Errors: new Dictionary<string, string[]>
                {
                    ["timeZone"] = ["A valid IANA time zone is required."]
                });
        }

        var local = TimeZoneInfo.ConvertTime(
            timeProvider.GetUtcNow(), zone!);
        var today = DateOnly.FromDateTime(local.DateTime);
        var tasks = await context.Tasks
            .Where(task => task.Project.ArchivedAt == null &&
                task.Project.Workspace.Members.Any(member =>
                    member.UserId == actorUserId &&
                    member.RemovedAt == null) &&
                (task.Project.Workspace.Type == WorkspaceTypeValues.Personal ||
                 task.Assignees.Any(link => link.UserId == actorUserId)))
            .Include(task => task.Assignees)
            .ThenInclude(link => link.User)
            .Include(task => task.TaskLabels)
            .ThenInclude(link => link.Label)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var taskIds = tasks.Select(task => task.Id).ToArray();
        var activity = taskIds.Length == 0
            ? []
            : await context.TaskActivityLogs
                .Where(item => taskIds.Contains(item.TaskId) &&
                    (item.EventType == TaskActivityTypes.Created ||
                     item.EventType == TaskActivityTypes.Updated))
                .AsNoTracking()
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);
        var completions = activity
            .Where(IsCompletion)
            .GroupBy(item => item.TaskId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(item => item.CreatedAt));

        var recentlyCompleted = new List<TaskSummary>();
        var overdue = new List<TaskSummary>();
        var todayItems = new List<TaskSummary>();
        var upcoming = new List<TaskSummary>();
        var later = new List<TaskSummary>();
        var unscheduled = new List<TaskSummary>();

        foreach (var task in tasks)
        {
            var summary = ToSummary(task);
            if (task.Status == WorkItemStatusValues.Done)
            {
                if (completions.TryGetValue(task.Id, out var completedAt))
                {
                    var localCompletion = TimeZoneInfo.ConvertTime(
                        completedAt, zone!);
                    var completedDate = DateOnly.FromDateTime(
                        localCompletion.DateTime);
                    if (completedDate >= today.AddDays(-6) &&
                        completedDate <= today)
                    {
                        recentlyCompleted.Add(summary);
                    }
                }
                continue;
            }

            if (task.DueDate is null)
                unscheduled.Add(summary);
            else if (task.DueDate < today)
                overdue.Add(summary);
            else if (task.DueDate == today)
                todayItems.Add(summary);
            else if (task.DueDate <= today.AddDays(7))
                upcoming.Add(summary);
            else
                later.Add(summary);
        }

        return new(
            WorkItemResultStatus.Success,
            new MyWorkSummary(
                Order(recentlyCompleted),
                Order(overdue),
                Order(todayItems),
                Order(upcoming),
                Order(later),
                Order(unscheduled)));
    }

    private static bool TryTimeZone(
        string? timeZone,
        out TimeZoneInfo? zone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            zone = null;
            return false;
        }
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            zone = null;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            zone = null;
            return false;
        }
    }

    private static bool IsCompletion(TaskActivityLog activity)
    {
        var details = activity.Details.RootElement;
        if (activity.EventType == TaskActivityTypes.Created)
        {
            return details.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String &&
                status.GetString() == WorkItemStatusValues.Done;
        }

        return details.TryGetProperty("changes", out var changes) &&
            changes.TryGetProperty("status", out var statusChange) &&
            statusChange.TryGetProperty("to", out var target) &&
            target.ValueKind == JsonValueKind.String &&
            target.GetString() == WorkItemStatusValues.Done;
    }

    private static List<TaskSummary> Order(
        IEnumerable<TaskSummary> tasks) => tasks
        .OrderBy(task => task.DueDate)
        .ThenBy(task => task.Title)
        .ThenBy(task => task.Id)
        .ToList();

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
}
