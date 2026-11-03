using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Tests;

public sealed class LoginApiTests
{
    [Fact]
    public async Task LoginReturnsBearerAccessToken()
    {
        var service = new FakeLoginService
        {
            Result = new LoginResult("header.payload.signature", 900)
        };
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new
            {
                email = "Person@example.com",
                password = "Correct-Horse-7-Battery!"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "{\"accessToken\":\"header.payload.signature\",\"tokenType\":\"Bearer\",\"expiresIn\":900}",
            await response.Content.ReadAsStringAsync());
        Assert.Equal("Person@example.com", service.Email);
    }

    [Fact]
    public async Task RejectedLoginsReturnTheSameSafeProblemContract()
    {
        var service = new FakeLoginService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var first = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "missing@example.com", password = "First-Secret-7!" });
        using var second = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "locked@example.com", password = "Second-Secret-7!" });

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal("application/problem+json",
            first.Content.Headers.ContentType?.MediaType);
        using var firstBody = JsonDocument.Parse(
            await first.Content.ReadAsStringAsync());
        using var secondBody = JsonDocument.Parse(
            await second.Content.ReadAsStringAsync());
        foreach (var field in new[] { "type", "title", "status", "detail" })
        {
            Assert.Equal(
                firstBody.RootElement.GetProperty(field).GetRawText(),
                secondBody.RootElement.GetProperty(field).GetRawText());
        }
        Assert.True(firstBody.RootElement.TryGetProperty("traceId", out _));
        var bodies = firstBody.RootElement.GetRawText() +
            secondBody.RootElement.GetRawText();
        Assert.DoesNotContain("missing@example.com", bodies,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locked@example.com", bodies,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", bodies,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidRequestDoesNotReachLoginService()
    {
        var service = new FakeLoginService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateClient();

        using var invalidEmail = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "not-an-email", password = "First-Secret-7!" });
        using var missingPassword = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = "person@example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, invalidEmail.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingPassword.StatusCode);
        Assert.Equal(0, service.CallCount);
    }

    private static FoundationFactory CreateFactory(FakeLoginService service) =>
        new(configureServices: services =>
        {
            services.RemoveAll<IAccountLoginService>();
            services.AddSingleton<IAccountLoginService>(service);
        });

    private sealed class FakeLoginService : IAccountLoginService
    {
        public LoginResult Result { get; set; } = LoginResult.Rejected;
        public string? Email { get; private set; }
        public int CallCount { get; private set; }

        public Task<LoginResult> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Email = email;
            return Task.FromResult(Result);
        }
    }
}
