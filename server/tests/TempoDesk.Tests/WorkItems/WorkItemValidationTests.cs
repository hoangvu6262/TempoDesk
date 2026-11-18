using TempoDesk.Application.WorkItems;

namespace TempoDesk.Tests.WorkItems;

public sealed class WorkItemValidationTests
{
    [Fact]
    public void TaskValidationReturnsFieldErrors()
    {
        var errors = WorkItemValidation.Task(new TaskMutation(
            " ",
            null,
            "invalid",
            "invalid",
            new DateOnly(2026, 11, 18),
            new DateOnly(2026, 11, 17),
            0,
            null));

        Assert.Contains("title", errors.Keys);
        Assert.Contains("priority", errors.Keys);
        Assert.Contains("status", errors.Keys);
        Assert.Contains("dueDate", errors.Keys);
        Assert.Contains("estimatedMinutes", errors.Keys);
    }

    [Theory]
    [InlineData(" Backend ", "Backend", "BACKEND")]
    [InlineData("backend", "backend", "BACKEND")]
    public void LabelNormalizationIsCaseInsensitive(
        string input,
        string display,
        string normalized)
    {
        var result = WorkItemValidation.Label(input);

        Assert.Equal(display, result.Value);
        Assert.Equal(normalized, result.NormalizedValue);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TextLimitsAreValidatedAtApplicationBoundary()
    {
        Assert.Contains("name", WorkItemValidation.ProjectName(
            new string('p', WorkItemLimits.ProjectName + 1)).Errors.Keys);
        Assert.Contains("title", WorkItemValidation.Task(new TaskMutation(
            new string('t', WorkItemLimits.TaskTitle + 1),
            null,
            WorkItemPriorityValues.None,
            WorkItemStatusValues.Todo,
            null,
            null,
            null,
            null)).Keys);
        Assert.Contains("name", WorkItemValidation.Label(
            new string('l', WorkItemLimits.LabelName + 1)).Errors.Keys);
    }
}
