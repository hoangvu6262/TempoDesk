using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.WorkItems.Persistence;

public sealed class WorkTask
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Priority { get; set; } = WorkItemPriorityValues.None;
    public string Status { get; set; } = WorkItemStatusValues.Todo;
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? EstimatedMinutes { get; set; }
    public string SourceType { get; set; } = WorkItemSourceTypeValues.Manual;
    public decimal SortKey { get; set; }
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Project Project { get; set; } = null!;
    public WorkTask? Parent { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public ICollection<WorkTask> Children { get; set; } = [];
    public ICollection<TaskAssignee> Assignees { get; set; } = [];
    public ICollection<TaskLabel> TaskLabels { get; set; } = [];
    public ICollection<TaskComment> Comments { get; set; } = [];
    public ICollection<TaskActivityLog> ActivityLogs { get; set; } = [];
}
