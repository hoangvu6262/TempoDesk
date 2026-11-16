using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.WorkItems.Persistence;

public sealed class TaskComment
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid TaskId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public WorkTask Task { get; set; } = null!;
    public ApplicationUser AuthorUser { get; set; } = null!;
}
