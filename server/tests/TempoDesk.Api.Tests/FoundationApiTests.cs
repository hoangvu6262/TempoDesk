using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TempoDesk.Api.Tests;

public sealed class FoundationApiTests
{
    [Fact]
    public void ProductionConfigurationDeclaresDisabledEmailDefaults()
    {
        using var factory = new FoundationFactory("Production");
        using var scope = factory.Services.CreateScope();
        var email = scope.ServiceProvider
            .GetRequiredService<IConfiguration>()
            .GetSection("Email");

        Assert.True(email.Exists());
        Assert.False(email.GetValue<bool>("Enabled"));
        Assert.Equal(587, email.GetValue<int>("Port"));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    public async Task ProblemDetailsMapsSupportedErrors(int status)
    {
        await using var factory = new FoundationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/dev/errors/{status}");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var field in new[] { "type", "title", "status", "detail", "traceId" })
            Assert.True(json.RootElement.TryGetProperty(field, out _), field);
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        if (status == 400) Assert.True(json.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ProblemDetailsProductionHidesDiagnosticsAndSanitizesFailures()
    {
        await using var factory = new FoundationFactory("Production");
        using var client = factory.CreateClient();
        using var missing = await client.GetAsync("/dev/errors/500");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var response = await client.GetAsync("/test/failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoggingCorrelatesTraceAndOnlyRecordsTemplateAndSafeFields()
    {
        await using var factory = new FoundationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/dev/errors/500?token=query-secret");
        request.Headers.Add("Authorization", "Bearer header-secret");
        request.Headers.Add("Cookie", "refresh-token=cookie-secret");
        request.Headers.Add("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
        using var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var log = Assert.Single(factory.Logs.Entries, entry => entry.ContainsKey("ElapsedMs"));
        Assert.Equal(body.RootElement.GetProperty("traceId").GetString(), log["TraceId"]);
        Assert.Equal("0123456789abcdef0123456789abcdef", log["TraceId"]);
        Assert.Equal("/dev/errors/{status:int}", log["Route"]);
        Assert.IsType<double>(log["ElapsedMs"]);
        Assert.DoesNotContain("secret", string.Join(' ', factory.Logs.Messages), StringComparison.OrdinalIgnoreCase);
        using var live = await client.GetAsync("/health/live");
        Assert.Single(factory.Logs.Entries, entry => entry.ContainsKey("ElapsedMs"));
    }

    [Fact]
    public async Task HealthLivenessWorksWhileDatabaseReadinessFailsSafely()
    {
        await using var factory = new FoundationFactory();
        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        using var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.GetProperty("elapsedMs").GetDouble() >= 0);
        Assert.DoesNotContain("secret", body.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProblemDetailsPreservesValidationFieldsAndNativeStatusErrors()
    {
        await using var factory = new FoundationFactory();
        using var client = factory.CreateClient();
        using var invalid = await client.PostAsJsonAsync("/test/validation", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var body = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
        using var conflict = await client.GetAsync("/test/conflict");
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var conflictBody = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.True(conflictBody.RootElement.TryGetProperty("traceId", out _));
    }
}

public sealed class FoundationFactory(
    string environment = "Development",
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    public RecordingLogs Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Password"] = "database-secret", ["Database:Host"] = "127.0.0.1",
            ["Database:Port"] = "1", ["Database:CommandTimeoutSeconds"] = "1"
        }));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(Logs);
            services.AddControllers().AddApplicationPart(typeof(FailureController).Assembly);
            configureServices?.Invoke(services);
        });
    }
}

[ApiController]
public sealed class FailureController : ControllerBase
{
    [HttpGet("/test/failure")]
    public IActionResult Fail() => throw new InvalidOperationException($"SELECT password FROM secret; token=secret {Request.Method}");

    [HttpPost("/test/validation")]
    public IActionResult Validate(ValidationRequest request) => Ok(request);

    [HttpGet("/test/conflict")]
    public IActionResult NativeConflict() => StatusCode(409);
}

public sealed class ValidationRequest
{
    [Required]
    public string? Name { get; set; }
}

public sealed class RecordingLogs : ILoggerProvider
{
    public ConcurrentQueue<Dictionary<string, object?>> Entries { get; } = new();
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Recorder(this);
    public void Dispose() { }

    private sealed class Recorder(RecordingLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            owner.Messages.Enqueue(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                owner.Entries.Enqueue(fields.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        }
    }
}
