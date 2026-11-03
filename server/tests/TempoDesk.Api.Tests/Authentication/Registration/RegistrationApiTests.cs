using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Tests;

public sealed class RegistrationApiTests
{
    [Fact]
    public async Task RegisterReturnsAcceptedWithoutExposingWhetherEmailExists()
    {
        var service = new FakeRegistrationService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "Person@example.com", password = "Correct-Horse-7-Battery!" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            "{\"message\":\"If the address can be verified, an email will be sent.\"}",
            await response.Content.ReadAsStringAsync());
        Assert.Equal("Person@example.com", service.RegisteredEmail);
    }

    [Fact]
    public async Task RegisterReturnsValidationProblemForRejectedCredentials()
    {
        var service = new FakeRegistrationService
        {
            RegistrationResult = new RegistrationResult(
                new Dictionary<string, string[]> { ["PasswordTooShort"] = ["Password is too short."] })
        };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "person@example.com", password = "short-password" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors")
            .TryGetProperty("PasswordTooShort", out _));
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task VerifyReturnsNoContentOnceAndValidationProblemOtherwise()
    {
        var service = new FakeRegistrationService { VerificationSucceeds = true };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var verified = await client.PostAsJsonAsync("/api/auth/verify-email",
            new { token = "valid-token" });
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);

        service.VerificationSucceeds = false;
        using var rejected = await client.PostAsJsonAsync("/api/auth/verify-email",
            new { token = "expired-token" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("token", out _));
    }

    [Fact]
    public async Task RegisterAndResendReturnTheSameGenericAcceptedResponse()
    {
        var service = new FakeRegistrationService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var registered = await client.PostAsJsonAsync("/api/auth/register",
            new { email = "existing@example.com", password = "Correct-Horse-7-Battery!" });
        using var response = await client.PostAsJsonAsync(
            "/api/auth/resend-verification", new { email = "missing@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            await registered.Content.ReadAsStringAsync(),
            await response.Content.ReadAsStringAsync());
        Assert.Equal("missing@example.com", service.ResentEmail);
    }

    private static FoundationFactory CreateFactory(FakeRegistrationService service) =>
        new(configureServices: services =>
        {
            services.RemoveAll<IAccountRegistrationService>();
            services.AddSingleton<IAccountRegistrationService>(service);
        });

    private sealed class FakeRegistrationService : IAccountRegistrationService
    {
        public RegistrationResult RegistrationResult { get; set; } =
            RegistrationResult.Accepted;
        public bool VerificationSucceeds { get; set; }
        public string? RegisteredEmail { get; private set; }
        public string? ResentEmail { get; private set; }

        public Task<RegistrationResult> RegisterAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default)
        {
            RegisteredEmail = email;
            return Task.FromResult(RegistrationResult);
        }

        public Task<bool> VerifyEmailAsync(
            string token,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(VerificationSucceeds);

        public Task ResendVerificationAsync(
            string email,
            CancellationToken cancellationToken = default)
        {
            ResentEmail = email;
            return Task.CompletedTask;
        }
    }
}
