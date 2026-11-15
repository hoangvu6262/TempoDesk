namespace TempoDesk.Application.Authentication;

public sealed record AuthenticationSession(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    AuthenticationSessionStatus Status,
    bool IsCurrent);
