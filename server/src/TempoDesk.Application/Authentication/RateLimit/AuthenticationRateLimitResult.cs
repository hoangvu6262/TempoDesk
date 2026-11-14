namespace TempoDesk.Application.Authentication;

public sealed record AuthenticationRateLimitResult(
    bool Allowed,
    TimeSpan RetryAfter);
