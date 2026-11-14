using Microsoft.Extensions.Options;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Application.Tests;

public sealed class AuthenticationRateLimiterTests
{
    [Fact]
    public void EnforcesCompositeLimitAndRecoversAfterWindow()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var options = new SecurityOptions
        {
            LoginIp = new() { PermitLimit = 10, WindowSeconds = 60 },
            LoginAccount = new() { PermitLimit = 2, WindowSeconds = 60 }
        };
        var limiter = new AuthenticationRateLimiter(
            Options.Create(options), clock);
        var request = new AuthenticationRateLimitRequest(
            AuthenticationRateLimitPolicy.Login,
            "203.0.113.10",
            AuthenticationRateLimitKeys.ForAccount("Person@example.com"));

        Assert.True(limiter.Acquire(request).Allowed);
        Assert.True(limiter.Acquire(request).Allowed);
        var denied = limiter.Acquire(request);
        Assert.False(denied.Allowed);
        Assert.True(denied.RetryAfter >= TimeSpan.FromSeconds(1));

        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(limiter.Acquire(request).Allowed);
    }

    [Fact]
    public void ConcurrentCallsDoNotExceedPermitLimit()
    {
        var options = new SecurityOptions
        {
            Refresh = new() { PermitLimit = 5, WindowSeconds = 60 }
        };
        var limiter = new AuthenticationRateLimiter(
            Options.Create(options), TimeProvider.System);
        var request = new AuthenticationRateLimitRequest(
            AuthenticationRateLimitPolicy.Refresh, "203.0.113.10");

        var allowed = Enumerable.Range(0, 100)
            .AsParallel()
            .Count(_ => limiter.Acquire(request).Allowed);

        Assert.Equal(5, allowed);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }
}
