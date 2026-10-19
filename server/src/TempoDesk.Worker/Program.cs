using Microsoft.Extensions.Options;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Health;
using TempoDesk.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddFoundationConfiguration(builder.Configuration);
if (args.Contains("--health-check", StringComparer.Ordinal))
{
    var settings = builder.Configuration.GetSection("BackgroundJobs").Get<BackgroundJobsOptions>() ?? new();
    return await WorkerHeartbeat.IsFreshAsync(settings.HeartbeatPath) ? 0 : 1;
}
builder.Services.AddSingleton<DatabaseReadiness>();
builder.Services.AddHostedService<WorkerHeartbeat>();
using var host = builder.Build();
await host.RunAsync();
return 0;
