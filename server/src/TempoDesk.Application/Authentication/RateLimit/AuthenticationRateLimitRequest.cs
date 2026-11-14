namespace TempoDesk.Application.Authentication;

public sealed record AuthenticationRateLimitRequest(
    AuthenticationRateLimitPolicy Policy,
    string IpAddress,
    string? AccountKey = null);
