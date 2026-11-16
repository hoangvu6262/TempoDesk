namespace TempoDesk.Infrastructure.WorkItems.Persistence;

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
