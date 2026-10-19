using Microsoft.Extensions.Options;
using Npgsql;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Infrastructure.Health;

public sealed class DatabaseReadiness(IOptions<DatabaseOptions> options)
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = settings.Host, Port = settings.Port, Database = settings.Name,
            Username = settings.Username, Password = settings.Password,
            Timeout = settings.CommandTimeoutSeconds, CommandTimeout = settings.CommandTimeoutSeconds,
            Pooling = false, GssEncryptionMode = GssEncryptionMode.Disable
        }.ConnectionString;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            return await command.ExecuteScalarAsync(cancellationToken) is 1;
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }
}
