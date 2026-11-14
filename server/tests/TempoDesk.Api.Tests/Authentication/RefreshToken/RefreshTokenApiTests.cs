using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Tests;

public sealed class RefreshTokenApiTests
{
    [Fact]
    public async Task RefreshReadsCookieAndReturnsNewTokenPair()
    {
        var service = new FakeTokenService
        {
            Result = new AuthenticationTokenResult(
                "new.access.token",
                900,
                "new-refresh-token",
                new DateTimeOffset(2026, 12, 3, 10, 0, 0,
                    TimeSpan.Zero))
        };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        client.BaseAddress = new Uri("https://localhost");
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add(
            "Cookie", "tempodesk.refresh_token=old-refresh-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "{\"accessToken\":\"new.access.token\",\"tokenType\":\"Bearer\",\"expiresIn\":900}",
            await response.Content.ReadAsStringAsync());
        Assert.Equal("old-refresh-token", service.RawToken);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("tempodesk.refresh_token=new-refresh-token", cookie);
        Assert.Contains("httponly", cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectedRefreshReturnsGenericUnauthorizedAndExpiresCookie()
    {
        var service = new FakeTokenService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add(
            "Cookie", "tempodesk.refresh_token=invalid-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("tempodesk.refresh_token=", cookie);
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie,
            StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("invalid-token", body);
    }

    private static FoundationFactory CreateFactory(FakeTokenService service) =>
        new(configureServices: services =>
        {
            services.RemoveAll<IAuthenticationTokenService>();
            services.AddSingleton<IAuthenticationTokenService>(service);
        });

    private sealed class FakeTokenService : IAuthenticationTokenService
    {
        public AuthenticationTokenResult? Result { get; set; }
        public string? RawToken { get; private set; }

        public Task<AuthenticationTokenResult> IssueAsync(
            Guid userId,
            SessionMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthenticationTokenResult?> RefreshAsync(
            string rawToken,
            CancellationToken cancellationToken = default)
        {
            RawToken = rawToken;
            return Task.FromResult(Result);
        }
    }
}
