namespace TempoDesk.Api.Controllers;

public sealed record SessionResponseDto(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string Status);
