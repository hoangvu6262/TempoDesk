namespace TempoDesk.Application.Authentication;

public sealed record NewRefreshToken(
    Guid Id,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);
