using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Tests;

public sealed class LogoutApiTests
{
    [Theory]
    [InlineData("current-refresh-token")]
    [InlineData(null)]
    public async Task LogoutIsIdempotentAndExpiresCookie(string? rawToken)
    {
        var service = new FakeSessionService();
        await using var factory = new FoundationFactory(
            configureServices: services =>
            {
                services.RemoveAll<IAuthenticationSessionService>();
                services.AddSingleton<IAuthenticationSessionService>(service);
            });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/auth/logout");
        if (rawToken is not null)
            request.Headers.Add(
                "Cookie", $"tempodesk.refresh_token={rawToken}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(rawToken ?? "", service.RawToken);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("tempodesk.refresh_token=", cookie);
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeSessionService : IAuthenticationSessionService
    {
        public string? RawToken { get; private set; }

        public Task LogoutAsync(
            string rawToken,
            CancellationToken cancellationToken = default)
        {
            RawToken = rawToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuthenticationSession>> ListAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RevokeAsync(
            Guid userId,
            Guid sessionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RevokeAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
