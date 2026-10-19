using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TempoDesk.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Name { get; set; } = "tempodesk";
    public string Username { get; set; } = "tempodesk_runtime";
    public string Password { get; set; } = "";
    public int CommandTimeoutSeconds { get; set; } = 5;
}

public sealed class AuthenticationOptions
{
    public bool Enabled { get; set; }
    public string Authority { get; set; } = "";
    public string Audience { get; set; } = "";
}

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173"];
}

public sealed class JiraOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "";
    public string Token { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class EmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string From { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class FileStorageOptions
{
    public bool Enabled { get; set; }
    public string RootPath { get; set; } = "";
    public long MaxFileSizeBytes { get; set; } = 10_485_760;
}

public sealed class BackgroundJobsOptions
{
    public bool Enabled { get; set; } = true;
    public int HeartbeatIntervalSeconds { get; set; } = 5;
    public string HeartbeatPath { get; set; } = Path.Combine(Path.GetTempPath(), "tempodesk-worker-health.json");
}

public static class FoundationConfiguration
{
    public static IServiceCollection AddFoundationConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        Bind<DatabaseOptions>("Database", o => !string.IsNullOrWhiteSpace(o.Host) && o.Port is > 0 and <= 65535 &&
            !string.IsNullOrWhiteSpace(o.Name) && !string.IsNullOrWhiteSpace(o.Username) &&
            !string.IsNullOrWhiteSpace(o.Password) && o.CommandTimeoutSeconds is >= 1 and <= 60);
        Bind<AuthenticationOptions>("Authentication", o => !o.Enabled || (HttpUrl(o.Authority) && !string.IsNullOrWhiteSpace(o.Audience)));
        Bind<CorsOptions>("Cors", o => o.AllowedOrigins.Length > 0 && o.AllowedOrigins.All(HttpUrl));
        Bind<JiraOptions>("Jira", o => !o.Enabled || (HttpUrl(o.BaseUrl) && !string.IsNullOrWhiteSpace(o.Token) && o.TimeoutSeconds is >= 1 and <= 300));
        Bind<EmailOptions>("Email", o => !o.Enabled || (!string.IsNullOrWhiteSpace(o.Host) && o.Port is > 0 and <= 65535 && System.Net.Mail.MailAddress.TryCreate(o.From, out _)));
        Bind<FileStorageOptions>("FileStorage", o => !o.Enabled || (Path.IsPathFullyQualified(o.RootPath) && o.MaxFileSizeBytes > 0));
        Bind<BackgroundJobsOptions>("BackgroundJobs", o => o.HeartbeatIntervalSeconds is >= 1 and <= 10 && Path.IsPathFullyQualified(o.HeartbeatPath));
        return services;

        void Bind<T>(string section, Func<T, bool> valid) where T : class => services.AddOptions<T>()
            .Bind(configuration.GetSection(section)).Validate(valid, $"{section} settings are missing or invalid.").ValidateOnStart();
    }

    private static bool HttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
}
