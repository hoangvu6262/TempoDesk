namespace TempoDesk.Application.Workspaces;

public sealed record WorkspaceInvitationEmail(
    string Email,
    string WorkspaceName,
    string Token,
    DateTimeOffset ExpiresAt);
