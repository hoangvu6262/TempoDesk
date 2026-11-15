using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public sealed class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public Guid OwnerUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ApplicationUser Owner { get; set; } = null!;
    public ICollection<WorkspaceMember> Members { get; set; } = [];
    public ICollection<WorkspaceInvitation> Invitations { get; set; } = [];
}
