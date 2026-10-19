using System.Text.Json;
using Microsoft.Extensions.Options;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Health;

namespace TempoDesk.Worker;

public sealed record Heartbeat(string Status, DateTimeOffset CheckedAt);

public sealed partial class WorkerHeartbeat(DatabaseReadiness database, IOptions<BackgroundJobsOptions> options,
    ILogger<WorkerHeartbeat> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        Configured(settings.Enabled);
        while (!stoppingToken.IsCancellationRequested)
        {
            var healthy = await database.IsReadyAsync(stoppingToken);
            try
            {
                var temporary = settings.HeartbeatPath + ".tmp";
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Heartbeat(
                    healthy ? "Healthy" : "Unhealthy", DateTimeOffset.UtcNow)), stoppingToken);
                File.Move(temporary, settings.HeartbeatPath, overwrite: true);
            }
            catch (IOException) { WriteFailed(); }
            catch (UnauthorizedAccessException) { WriteFailed(); }
            await Task.Delay(TimeSpan.FromSeconds(settings.HeartbeatIntervalSeconds), stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker configured: BackgroundJobs={BackgroundJobsEnabled}")]
    private partial void Configured(bool backgroundJobsEnabled);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker heartbeat could not be written")]
    private partial void WriteFailed();

    public static async Task<bool> IsFreshAsync(string path)
    {
        try
        {
            var heartbeat = JsonSerializer.Deserialize<Heartbeat>(await File.ReadAllTextAsync(path));
            var age = DateTimeOffset.UtcNow - heartbeat?.CheckedAt;
            return heartbeat?.Status == "Healthy" && age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(30);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
