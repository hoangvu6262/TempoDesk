namespace TempoDesk.Application.Workspaces;

public sealed record WorkspaceMemberSummary(
    Guid Id,
    string Email,
    string Role,
    DateTimeOffset JoinedAt);

public sealed record WorkspaceInvitationSummary(
    Guid Id,
    string Email,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
