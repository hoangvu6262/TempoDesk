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

    private static ServiceProvider Create(Dictionary<string, string?> values) => new ServiceCollection()
        .AddFoundationConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();

    [Theory]
    [InlineData("@@")]
    [InlineData("invalid email")]
    public void ConfigurationRejectsInvalidEmailAddress(string address)
    {
        using var provider = Create(new()
        {
            ["Email:Enabled"] = "true", ["Email:Host"] = "localhost", ["Email:From"] = address
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
}
