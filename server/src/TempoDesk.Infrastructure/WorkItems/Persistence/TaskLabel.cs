namespace TempoDesk.Infrastructure.WorkItems.Persistence;

public sealed class TaskLabel
{
    public Guid WorkspaceId { get; set; }
    public Guid TaskId { get; set; }
    public Guid LabelId { get; set; }
    public WorkTask Task { get; set; } = null!;
    public Label Label { get; set; } = null!;
}
