namespace TempoDesk.Application.Authentication;

public interface IAuthenticationAuditService
{
    Task RecordRateLimitedAsync(
        AuthenticationRateLimitPolicy policy,
        CancellationToken cancellationToken = default);
}
