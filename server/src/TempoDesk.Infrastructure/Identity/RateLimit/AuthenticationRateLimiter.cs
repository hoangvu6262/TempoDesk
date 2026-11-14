using Microsoft.Extensions.Options;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Infrastructure.Identity;

public sealed class AuthenticationRateLimiter(
    IOptions<SecurityOptions> configured,
    TimeProvider timeProvider) : IAuthenticationRateLimiter
{
    private readonly Dictionary<string, Window> windows = [];
    private readonly object gate = new();

    public AuthenticationRateLimitResult Acquire(
        AuthenticationRateLimitRequest request)
    {
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var expired in windows
                .Where(pair => pair.Value.EndsAt <= now)
                .Select(pair => pair.Key)
                .ToArray())
                windows.Remove(expired);

            var partitions = Partitions(request).ToArray();
            var blocked = partitions
                .Where(partition => Current(partition, now).Count >=
                    partition.Options.PermitLimit)
                .Select(partition => Current(partition, now).EndsAt - now)
                .ToArray();
            if (blocked.Length > 0)
                return new(false, blocked.Max());

            foreach (var partition in partitions)
                Current(partition, now).Count++;
            return new(true, TimeSpan.Zero);
        }
    }

    private IEnumerable<Partition> Partitions(
        AuthenticationRateLimitRequest request)
    {
        var options = configured.Value;
        yield return new Partition(
            $"{request.Policy}:ip:{request.IpAddress}",
            request.Policy switch
            {
                AuthenticationRateLimitPolicy.Register => options.Register,
                AuthenticationRateLimitPolicy.Login => options.LoginIp,
                AuthenticationRateLimitPolicy.ForgotPassword =>
                    options.ForgotPasswordIp,
                _ => options.Refresh
            });
        if (request.Policy is AuthenticationRateLimitPolicy.Login or
            AuthenticationRateLimitPolicy.ForgotPassword)
            yield return new Partition(
                $"{request.Policy}:account:{request.AccountKey ?? "unknown"}",
                request.Policy == AuthenticationRateLimitPolicy.Login
                    ? options.LoginAccount
                    : options.ForgotPasswordAccount);
    }

    private Window Current(Partition partition, DateTimeOffset now)
    {
        if (!windows.TryGetValue(partition.Key, out var window))
        {
            window = new Window(
                now.AddSeconds(partition.Options.WindowSeconds));
            windows.Add(partition.Key, window);
        }
        return window;
    }

    private sealed record Partition(
        string Key,
        AuthenticationRateLimitOptions Options);

    private sealed class Window(DateTimeOffset endsAt)
    {
        public DateTimeOffset EndsAt { get; } = endsAt;
        public int Count { get; set; }
    }
}
