extern alias worker;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Health;
using Testcontainers.PostgreSql;
using WorkerHeartbeat = worker::TempoDesk.Worker.WorkerHeartbeat;
using Heartbeat = worker::TempoDesk.Worker.Heartbeat;

namespace TempoDesk.IntegrationTests;

public sealed class FoundationIntegrationTests
{
    [Fact]
    public async Task PostgreSqlReadinessDetectsOutageAndRecovery()
    {
        await using var database = new PostgreSqlBuilder("postgres:18.6-bookworm")
            .WithDatabase("foundation_test").WithUsername("foundation_runtime").WithPassword(Guid.NewGuid().ToString("N")).Build();
        await database.StartAsync();
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(database.GetConnectionString());
        var settings = new DatabaseOptions
        {
            Host = database.Hostname, Port = database.GetMappedPublicPort(5432), Name = connection.Database!,
            Username = connection.Username!, Password = connection.Password!, CommandTimeoutSeconds = 1
        };
        await using var factory = new DatabaseApiFactory(settings);
        using var client = factory.CreateClient();
        var readiness = new DatabaseReadiness(Options.Create(settings));
        var heartbeatPath = Path.Combine(Path.GetTempPath(), $"tempodesk-worker-{Guid.NewGuid():N}.json");
        using var heartbeat = new WorkerHeartbeat(readiness, Options.Create(new BackgroundJobsOptions
        {
            HeartbeatPath = heartbeatPath, HeartbeatIntervalSeconds = 1
        }), NullLogger<WorkerHeartbeat>.Instance);
        await heartbeat.StartAsync(CancellationToken.None);
        var paused = false;
        try
        {
            Assert.True(await readiness.IsReadyAsync());
            await WaitForHeartbeatAsync(heartbeatPath, "Healthy");
            using (var healthy = await client.GetAsync("/health/ready")) Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            await database.PauseAsync();
            paused = true;
            Assert.False(await readiness.IsReadyAsync());
            await WaitForHeartbeatAsync(heartbeatPath, "Unhealthy");
            using (var unhealthy = await client.GetAsync("/health/ready")) Assert.Equal(HttpStatusCode.ServiceUnavailable, unhealthy.StatusCode);
            using (var live = await client.GetAsync("/health/live")) Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            await database.UnpauseAsync();
            paused = false;
            await WaitForHeartbeatAsync(heartbeatPath, "Healthy");
            Assert.True(await readiness.IsReadyAsync());
            using var recovered = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        }
        finally
        {
            if (paused) await database.UnpauseAsync();
            await heartbeat.StopAsync(CancellationToken.None);
            File.Delete(heartbeatPath);
            File.Delete(heartbeatPath + ".tmp");
        }
    }

    private static async Task WaitForHeartbeatAsync(string path, string status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!timeout.IsCancellationRequested)
        {
            if (File.Exists(path))
            {
                var value = JsonSerializer.Deserialize<Heartbeat>(await File.ReadAllTextAsync(path, timeout.Token));
                if (value?.Status == status) return;
            }
            await Task.Delay(100, timeout.Token);
        }
        Assert.Fail($"Worker did not report {status}.");
    }

    [Fact]
    public async Task WorkerHealthCheckRejectsMissingStaleUnhealthyAndMalformedHeartbeat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tempodesk-heartbeat-{Guid.NewGuid():N}.json");
        try
        {
            Assert.False(await WorkerHeartbeat.IsFreshAsync(path));
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Heartbeat("Healthy", DateTimeOffset.UtcNow)));
            Assert.True(await WorkerHeartbeat.IsFreshAsync(path));
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Heartbeat("Healthy", DateTimeOffset.UtcNow.AddSeconds(-31))));
            Assert.False(await WorkerHeartbeat.IsFreshAsync(path));
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new Heartbeat("Unhealthy", DateTimeOffset.UtcNow)));
            Assert.False(await WorkerHeartbeat.IsFreshAsync(path));
            await File.WriteAllTextAsync(path, "malformed");
            Assert.False(await WorkerHeartbeat.IsFreshAsync(path));
        }
        finally { File.Delete(path); }
    }

    private sealed class DatabaseApiFactory(DatabaseOptions settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = settings.Host,
                ["Database:Port"] = settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Database:Name"] = settings.Name, ["Database:Username"] = settings.Username,
                ["Database:Password"] = settings.Password, ["Database:CommandTimeoutSeconds"] = "1"
            }));
        }
    }
}
