using System.Text.Json;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class AuthenticationAuditService(
    TempoDeskIdentityDbContext context,
    TimeProvider timeProvider) : IAuthenticationAuditService
{
    public async Task RecordRateLimitedAsync(
        AuthenticationRateLimitPolicy policy,
        CancellationToken cancellationToken = default)
    {
        context.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            EventType = "auth.rate_limited",
            Succeeded = false,
            Metadata = JsonSerializer.Serialize(new
            {
                policy = policy.ToString().ToLowerInvariant()
            }),
            OccurredAt = timeProvider.GetUtcNow()
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
