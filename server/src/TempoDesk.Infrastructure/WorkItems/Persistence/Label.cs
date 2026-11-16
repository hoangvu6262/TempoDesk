using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.WorkItems.Persistence;

public sealed class Label
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public ICollection<TaskLabel> TaskLabels { get; set; } = [];
}
