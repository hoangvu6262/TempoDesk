using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public sealed class WorkspaceMember
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
