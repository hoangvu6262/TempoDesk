namespace TempoDesk.Application.Authentication;

public interface IAuthenticationRateLimiter
{
    AuthenticationRateLimitResult Acquire(
        AuthenticationRateLimitRequest request);
}
