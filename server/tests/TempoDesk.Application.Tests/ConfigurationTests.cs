using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Application.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("Database:Password", "")]
    [InlineData("Database:Port", "0")]
    [InlineData("Database:CommandTimeoutSeconds", "61")]
    [InlineData("BackgroundJobs:HeartbeatIntervalSeconds", "31")]
    [InlineData("Cors:AllowedOrigins:0", "*")]
    public void ConfigurationRejectsInvalidValuesWithoutLeakingSecrets(string key, string value)
    {
        using var provider = Create(new() { ["Database:Password"] = "private-password", [key] = value });
        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<BackgroundJobsOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        });
        Assert.DoesNotContain("private-password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationDisabledIntegrationsAcceptPlaceholdersAndDefaults()
    {
        using var provider = Create(new() { ["Database:Password"] = "private-password", ["Database:Port"] = "5433" });
        Assert.Equal(5433, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Port);
        Assert.Equal("tempodesk_runtime", provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Username);
        Assert.False(provider.GetRequiredService<IOptions<JiraOptions>>().Value.Enabled);
        Assert.False(provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value.Enabled);
        Assert.False(provider.GetRequiredService<IOptions<EmailOptions>>().Value.Enabled);
        Assert.False(provider.GetRequiredService<IOptions<FileStorageOptions>>().Value.Enabled);
    }

    [Theory]
    [InlineData("Jira")]
    [InlineData("Authentication")]
    [InlineData("Email")]
    [InlineData("FileStorage")]
    public void ConfigurationEnabledIntegrationsRequireSettings(string section)
    {
        using var provider = Create(new() { [$"{section}:Enabled"] = "true" });
        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<JiraOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value;
        });
    }

    [Theory]
    [InlineData("Authentication:Issuer", "")]
    [InlineData("Authentication:Audience", "")]
    [InlineData("Authentication:SigningKey", "short")]
    [InlineData("Authentication:AccessTokenMinutes", "0")]
    [InlineData("Authentication:AccessTokenMinutes", "61")]
    public void AuthenticationRejectsInvalidJwtSettings(string key, string value)
    {
        const string signingKey = "0123456789abcdef0123456789abcdef";
        using var provider = Create(new()
        {
            ["Database:Password"] = "private-password",
            ["Authentication:Enabled"] = "true",
            ["Authentication:Issuer"] = "https://api.tempodesk.local",
            ["Authentication:Audience"] = "tempodesk-api",
            ["Authentication:SigningKey"] = signingKey,
            ["Authentication:AccessTokenMinutes"] = "15",
            [key] = value
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value);

        Assert.DoesNotContain(signingKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationAcceptsValidJwtSettings()
    {
        const string signingKey = "0123456789abcdef0123456789abcdef";
        using var provider = Create(new()
        {
            ["Database:Password"] = "private-password",
            ["Authentication:Enabled"] = "true",
            ["Authentication:Issuer"] = "https://api.tempodesk.local",
            ["Authentication:Audience"] = "tempodesk-api",
            ["Authentication:SigningKey"] = signingKey,
            ["Authentication:AccessTokenMinutes"] = "15"
        });

        var options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal("https://api.tempodesk.local", options.Issuer);
        Assert.Equal("tempodesk-api", options.Audience);
        Assert.Equal(signingKey, options.SigningKey);
        Assert.Equal(15, options.AccessTokenMinutes);
    }

    private static ServiceProvider Create(Dictionary<string, string?> values) => new ServiceCollection()
        .AddFoundationConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();

    [Theory]
    [InlineData("@@")]
    [InlineData("invalid email")]
    public void ConfigurationRejectsInvalidEmailAddress(string address)
    {
        using var provider = Create(new()
        {
            ["Email:Enabled"] = "true", ["Email:Host"] = "localhost", ["Email:From"] = address,
            ["Email:FrontendBaseUrl"] = "http://localhost:5173"
        });
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EmailOptions>>().Value);
    }

    [Fact]
    public void ConfigurationEnvironmentVariablesOverrideDevelopmentDefaults()
    {
        var prefix = $"TEMPODESK_TEST_{Guid.NewGuid():N}_";
        Environment.SetEnvironmentVariable(prefix + "Database__Port", "5433");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Password"] = "test-secret" })
                .AddEnvironmentVariables(prefix).Build();
            using var provider = new ServiceCollection().AddFoundationConfiguration(configuration).BuildServiceProvider();
            Assert.Equal(5433, provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Port);
        }
        finally { Environment.SetEnvironmentVariable(prefix + "Database__Port", null); }
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("https://app.example/tempo")]
    [InlineData("https://app.example/tempo/")]
    public void EnabledEmailAcceptsValidFrontendBaseUrl(string frontendBaseUrl)
    {
        using var provider = Create(new()
        {
            ["Database:Password"] = "private-password",
            ["Email:Enabled"] = "true",
            ["Email:Host"] = "localhost",
            ["Email:From"] = "no-reply@tempodesk.local",
            ["Email:FrontendBaseUrl"] = frontendBaseUrl
        });

        Assert.Equal(
            frontendBaseUrl,
            provider.GetRequiredService<IOptions<EmailOptions>>()
                .Value.FrontendBaseUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//example.com")]
    [InlineData("https://app.example/tempo?lang=en")]
    [InlineData("https://app.example/tempo#section")]
    public void EnabledEmailRejectsInvalidFrontendBaseUrl(string frontendBaseUrl)
    {
        using var provider = Create(new()
        {
            ["Database:Password"] = "private-password",
            ["Email:Enabled"] = "true",
            ["Email:Host"] = "localhost",
            ["Email:From"] = "no-reply@tempodesk.local",
            ["Email:FrontendBaseUrl"] = frontendBaseUrl
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<EmailOptions>>().Value);
    }
}
