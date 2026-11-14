using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Tests;

public sealed class SessionApiTests
{
    private static readonly Guid UserId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task SessionRoutesRequireAuthentication()
    {
        var service = new FakeSessionService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var list = await client.GetAsync("/api/auth/sessions");
        using var revoke = await client.DeleteAsync(
            $"/api/auth/sessions/{Guid.NewGuid()}");
        using var revokeAll = await client.DeleteAsync("/api/auth/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revokeAll.StatusCode);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task ListReturnsOnlyTheAuthenticatedUsersSessionContract()
    {
        var sessionId = Guid.NewGuid();
        var service = new FakeSessionService
        {
            Sessions =
            [
                new AuthenticationSession(
                    sessionId,
                    "TempoDesk Agent",
                    "203.0.113.10",
                    new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 12, 1, 1, 0, 0, TimeSpan.Zero),
                    AuthenticationSessionStatus.Active)
            ]
        };
        await using var factory = CreateFactory(service);
        using var client = AuthenticatedClient(factory);

        using var response = await client.GetAsync("/api/auth/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(UserId, service.UserId);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var session = Assert.Single(body.RootElement.EnumerateArray());
        Assert.Equal(sessionId, session.GetProperty("id").GetGuid());
        Assert.Equal("TempoDesk Agent",
            session.GetProperty("userAgent").GetString());
        Assert.Equal("203.0.113.10",
            session.GetProperty("ipAddress").GetString());
        Assert.Equal("active", session.GetProperty("status").GetString());
        Assert.False(session.TryGetProperty("tokenHash", out _));
        Assert.False(session.TryGetProperty("refreshToken", out _));
    }

    [Theory]
    [InlineData(true, HttpStatusCode.NoContent)]
    [InlineData(false, HttpStatusCode.NotFound)]
    public async Task RevokeMapsOwnershipWithoutExposingOtherSessions(
        bool owned,
        HttpStatusCode expectedStatus)
    {
        var sessionId = Guid.NewGuid();
        var service = new FakeSessionService { RevokeResult = owned };
        await using var factory = CreateFactory(service);
        using var client = AuthenticatedClient(factory);

        using var response = await client.DeleteAsync(
            $"/api/auth/sessions/{sessionId}");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(UserId, service.UserId);
        Assert.Equal(sessionId, service.SessionId);
    }

    [Fact]
    public async Task RevokeAllUsesTheAuthenticatedUser()
    {
        var service = new FakeSessionService();
        await using var factory = CreateFactory(service);
        using var client = AuthenticatedClient(factory);

        using var response = await client.DeleteAsync("/api/auth/sessions");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(UserId, service.UserId);
    }

    private static HttpClient AuthenticatedClient(FoundationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", UserId.ToString());
        return client;
    }

    private static FoundationFactory CreateFactory(
        FakeSessionService service) => new(configureServices: services =>
        {
            services.RemoveAll<IAuthenticationSessionService>();
            services.AddSingleton<IAuthenticationSessionService>(service);
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                })
                .AddScheme<AuthenticationSchemeOptions,
                    TestAuthenticationHandler>("Test", _ => { });
        });

    private sealed class FakeSessionService : IAuthenticationSessionService
    {
        public IReadOnlyList<AuthenticationSession> Sessions { get; init; } = [];
        public bool RevokeResult { get; init; }
        public Guid? UserId { get; private set; }
        public Guid? SessionId { get; private set; }
        public int CallCount { get; private set; }

        public Task LogoutAsync(
            string rawToken,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AuthenticationSession>> ListAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            UserId = userId;
            return Task.FromResult(Sessions);
        }

        public Task<bool> RevokeAsync(
            Guid userId,
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            UserId = userId;
            SessionId = sessionId;
            return Task.FromResult(RevokeResult);
        }

        public Task RevokeAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            UserId = userId;
            return Task.CompletedTask;
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var authorization = Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith(
                    "Bearer ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier,
                    authorization["Bearer ".Length..])],
                Scheme.Name);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
