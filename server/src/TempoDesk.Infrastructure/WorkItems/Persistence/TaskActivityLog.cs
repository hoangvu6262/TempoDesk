using System.Text.Json;
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.WorkItems.Persistence;

public sealed class TaskActivityLog
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid TaskId { get; set; }
    public Guid ActorUserId { get; set; }
    public string EventType { get; set; } = "";
    public JsonDocument Details { get; set; } = JsonDocument.Parse("{}");
    public DateTimeOffset CreatedAt { get; set; }
    public WorkTask Task { get; set; } = null!;
    public ApplicationUser ActorUser { get; set; } = null!;
}
