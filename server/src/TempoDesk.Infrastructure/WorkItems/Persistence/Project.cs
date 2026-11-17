using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.WorkItems.Persistence;

public sealed class Project
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public ICollection<WorkTask> Tasks { get; set; } = [];
}
