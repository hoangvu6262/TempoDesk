namespace TempoDesk.Application.WorkItems;

public static class WorkItemValidation
{
    private static readonly HashSet<string> Priorities =
    [
        WorkItemPriorityValues.None,
        WorkItemPriorityValues.Low,
        WorkItemPriorityValues.Medium,
        WorkItemPriorityValues.High,
        WorkItemPriorityValues.Urgent
    ];

    private static readonly HashSet<string> Statuses =
    [
        WorkItemStatusValues.Todo,
        WorkItemStatusValues.InProgress,
        WorkItemStatusValues.Done
    ];

    public static TextValidationResult ProjectName(string? name) =>
        RequiredText(name, "name", WorkItemLimits.ProjectName);

    public static TextValidationResult Label(string? name)
    {
        var result = RequiredText(name, "name", WorkItemLimits.LabelName);
        return result.Value is null
            ? result
            : result with
            {
                NormalizedValue = result.Value.ToUpperInvariant()
            };
    }

    public static TextValidationResult Comment(string? body) =>
        RequiredText(body, "body", int.MaxValue);

    public static IReadOnlyDictionary<string, string[]> Task(
        TaskMutation task)
    {
        var errors = new Dictionary<string, string[]>();
        Add(errors, RequiredText(
            task.Title, "title", WorkItemLimits.TaskTitle).Errors);

        if (task.Priority is null || !Priorities.Contains(task.Priority))
            errors["priority"] = ["Priority is invalid."];

        if (task.Status is null || !Statuses.Contains(task.Status))
            errors["status"] = ["Status is invalid."];

        if (task.StartDate is not null && task.DueDate is not null &&
            task.DueDate < task.StartDate)
        {
            errors["dueDate"] =
                ["Due date cannot be before start date."];
        }

        if (task.EstimatedMinutes is <= 0)
        {
            errors["estimatedMinutes"] =
                ["Estimated minutes must be positive."];
        }

        return errors;
    }

    public static string? NormalizeOptional(string? value) =>
        value?.Trim();

    private static TextValidationResult RequiredText(
        string? value,
        string field,
        int maxLength)
    {
        var normalized = value?.Trim();
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrEmpty(normalized))
            errors[field] = [$"{Display(field)} is required."];
        else if (normalized.Length > maxLength)
            errors[field] = [$"{Display(field)} is too long."];

        return new(normalized, null, errors);
    }

    private static void Add(
        Dictionary<string, string[]> target,
        IReadOnlyDictionary<string, string[]> source)
    {
        foreach (var error in source)
            target[error.Key] = error.Value;
    }

    private static string Display(string field) =>
        char.ToUpperInvariant(field[0]) + field[1..];
}
