using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Tests;

public sealed class PasswordResetApiTests
{
    [Fact]
    public async Task ForgotPasswordAlwaysReturnsNeutralAcceptedResponse()
    {
        var service = new FakeResetService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new { email = "person@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            "{\"message\":\"If the account exists, a password reset email will be sent.\"}",
            await response.Content.ReadAsStringAsync());
        Assert.Equal("person@example.com", service.Email);
    }

    [Fact]
    public async Task ResetMapsSuccessInvalidTokenAndPolicyErrors()
    {
        var service = new FakeResetService
        {
            Result = ResetPasswordResult.Success
        };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var success = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = "valid-token", newPassword = "New-Password-7!" });
        service.Result = ResetPasswordResult.InvalidToken;
        using var invalid = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = "invalid-token", newPassword = "New-Password-7!" });
        service.Result = new ResetPasswordResult(
            false,
            false,
            new Dictionary<string, string[]>
            {
                ["PasswordTooShort"] = ["Passwords must be at least 12 characters."]
            });
        using var weak = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new { token = "valid-token", newPassword = "weak-password" });

        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("invalid or expired",
            await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("PasswordTooShort",
            await weak.Content.ReadAsStringAsync());
    }

    private static FoundationFactory CreateFactory(FakeResetService service) =>
        new(configureServices: services =>
        {
            services.RemoveAll<IAccountPasswordResetService>();
            services.AddSingleton<IAccountPasswordResetService>(service);
        });

    private sealed class FakeResetService : IAccountPasswordResetService
    {
        public string? Email { get; private set; }
        public ResetPasswordResult Result { get; set; } =
            ResetPasswordResult.InvalidToken;

        public Task ForgotPasswordAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            Email = email;
            return Task.CompletedTask;
        }

        public Task<ResetPasswordResult> ResetPasswordAsync(
            string token,
            string newPassword,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }
}
