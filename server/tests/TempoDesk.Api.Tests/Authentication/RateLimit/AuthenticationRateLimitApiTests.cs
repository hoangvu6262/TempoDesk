using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Tests;

public sealed class AuthenticationRateLimitApiTests
{
    [Fact]
    public async Task LoginBlocksAccountAfterApprovedThreshold()
    {
        var login = new FakeLoginService();
        var audit = new FakeAuditService();
        await using var factory = new FoundationFactory(
            configureServices: services =>
            {
                services.RemoveAll<IAccountLoginService>();
                services.AddSingleton<IAccountLoginService>(login);
                services.RemoveAll<IAuthenticationAuditService>();
                services.AddSingleton<IAuthenticationAuditService>(audit);
            });
        using var client = factory.CreateClient();

        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            response?.Dispose();
            response = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = "person@example.com",
                password = "Wrong-Password-7!"
            });
        }
        using (response = Assert.IsType<HttpResponseMessage>(response))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.True(int.Parse(
                Assert.Single(response.Headers.GetValues("Retry-After")),
                CultureInfo.InvariantCulture) >= 1);
        }
        Assert.Equal(5, login.CallCount);
        Assert.Equal(AuthenticationRateLimitPolicy.Login, audit.Policy);
    }

    [Fact]
    public async Task RegisterForgotAndRefreshUseTheirApprovedLimits()
    {
        var registration = new FakeRegistrationService();
        var resets = new FakeResetService();
        var tokens = new FakeTokenService();
        var audit = new FakeAuditService();
        await using var factory = new FoundationFactory(
            configureServices: services =>
            {
                services.RemoveAll<IAccountRegistrationService>();
                services.AddSingleton<IAccountRegistrationService>(registration);
                services.RemoveAll<IAccountPasswordResetService>();
                services.AddSingleton<IAccountPasswordResetService>(resets);
                services.RemoveAll<IAuthenticationTokenService>();
                services.AddSingleton<IAuthenticationTokenService>(tokens);
                services.RemoveAll<IAuthenticationAuditService>();
                services.AddSingleton<IAuthenticationAuditService>(audit);
            });
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 6; attempt++)
            using (await client.PostAsJsonAsync("/api/auth/register", new
            {
                email = $"person{attempt}@example.com",
                password = "Correct-Horse-7-Battery!"
            })) { }
        for (var attempt = 0; attempt < 4; attempt++)
            using (await client.PostAsJsonAsync("/api/auth/forgot-password", new
            {
                email = "forgot@example.com"
            })) { }
        for (var attempt = 0; attempt < 31; attempt++)
            using (await client.PostAsync("/api/auth/refresh", null)) { }

        Assert.Equal(5, registration.CallCount);
        Assert.Equal(3, resets.CallCount);
        Assert.Equal(30, tokens.CallCount);
    }

    private sealed class FakeLoginService : IAccountLoginService
    {
        public int CallCount { get; private set; }

        public Task<LoginResult> LoginAsync(
            string email,
            string password,
            SessionMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(LoginResult.Rejected);
        }
    }

    private sealed class FakeAuditService : IAuthenticationAuditService
    {
        public AuthenticationRateLimitPolicy? Policy { get; private set; }

        public Task RecordRateLimitedAsync(
            AuthenticationRateLimitPolicy policy,
            CancellationToken cancellationToken = default)
        {
            Policy = policy;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRegistrationService : IAccountRegistrationService
    {
        public int CallCount { get; private set; }
        public Task<RegistrationResult> RegisterAsync(
            string email, string password,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(RegistrationResult.Accepted);
        }
        public Task<bool> VerifyEmailAsync(
            string token, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ResendVerificationAsync(
            string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeResetService : IAccountPasswordResetService
    {
        public int CallCount { get; private set; }
        public Task ForgotPasswordAsync(
            string email, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
        public Task<ResetPasswordResult> ResetPasswordAsync(
            string token, string newPassword,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTokenService : IAuthenticationTokenService
    {
        public int CallCount { get; private set; }
        public Task<AuthenticationTokenResult> IssueAsync(
            Guid userId, SessionMetadata metadata,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AuthenticationTokenResult?> RefreshAsync(
            string rawToken, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<AuthenticationTokenResult?>(null);
        }
    }
}
