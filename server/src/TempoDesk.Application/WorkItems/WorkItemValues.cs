namespace TempoDesk.Application.WorkItems;

public static class WorkItemStatusValues
{
    public const string Todo = "todo";
    public const string InProgress = "in_progress";
    public const string Done = "done";
}

public static class WorkItemPriorityValues
{
    public const string None = "none";
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Urgent = "urgent";
}

public static class WorkItemSourceTypeValues
{
    public const string Manual = "manual";
    public const string Jira = "jira";
}

public static class WorkItemLimits
{
    public const int ProjectName = 200;
    public const int TaskTitle = 300;
    public const int LabelName = 100;
}

public static class TaskActivityTypes
{
    public const string Created = "task_created";
    public const string Updated = "task_updated";
    public const string AssigneesChanged = "assignees_changed";
    public const string LabelsChanged = "labels_changed";
    public const string CommentAdded = "comment_added";
    public const string Deleted = "task_deleted";
    public const string Restored = "task_restored";
}
