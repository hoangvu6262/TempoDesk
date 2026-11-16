namespace TempoDesk.Api.Controllers;

public sealed record WorkspaceMemberResponseDto(
    Guid Id,
    string Email,
    string Role,
    DateTimeOffset JoinedAt);

public sealed record WorkspaceInvitationResponseDto(
    Guid Id,
    string Email,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
