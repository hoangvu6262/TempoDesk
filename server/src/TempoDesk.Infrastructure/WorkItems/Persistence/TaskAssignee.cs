using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.WorkItems.Persistence;

public sealed class TaskAssignee
{
    public Guid WorkspaceId { get; set; }
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public WorkTask Task { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
