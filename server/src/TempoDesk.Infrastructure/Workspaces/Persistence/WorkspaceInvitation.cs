using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public sealed class WorkspaceInvitation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InvitedByUserId { get; set; }
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string Role { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public ApplicationUser InvitedByUser { get; set; } = null!;
}
