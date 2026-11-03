namespace TempoDesk.Application.Authentication;

public sealed record AuthenticationTokenResult(
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);
