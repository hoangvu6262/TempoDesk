using Microsoft.EntityFrameworkCore;
using Npgsql;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace TempoDesk.Tests.Database;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private const string MigratorPassword = "test-migrator-password";
    private const string RuntimePassword = "test-runtime-password";
    private readonly PostgreSqlContainer database =
        new PostgreSqlBuilder("postgres:18.6-bookworm")
            .WithDatabase("tempodesk_test")
            .WithUsername("postgres")
            .WithPassword("test-postgres-password")
            .Build();
    private string migratorConnectionString = "";

    public DatabaseOptions RuntimeOptions { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        var admin = new NpgsqlConnectionStringBuilder(
            database.GetConnectionString());

        await using (var connection = new NpgsqlConnection(
            admin.ConnectionString))
        {
            await connection.OpenAsync();
            const string bootstrap = """
                CREATE ROLE tempodesk_migrator LOGIN
                    PASSWORD 'test-migrator-password';
                CREATE ROLE tempodesk_runtime LOGIN
                    PASSWORD 'test-runtime-password';
                REVOKE ALL ON DATABASE tempodesk_test FROM PUBLIC;
                GRANT CONNECT ON DATABASE tempodesk_test
                    TO tempodesk_migrator, tempodesk_runtime;
                REVOKE ALL ON SCHEMA public FROM PUBLIC;
                ALTER SCHEMA public OWNER TO tempodesk_migrator;
                GRANT USAGE ON SCHEMA public TO tempodesk_runtime;
                ALTER DEFAULT PRIVILEGES FOR ROLE tempodesk_migrator
                    IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES
                    TO tempodesk_runtime;
                ALTER DEFAULT PRIVILEGES FOR ROLE tempodesk_migrator
                    IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES
                    TO tempodesk_runtime;
                """;
            await new NpgsqlCommand(bootstrap, connection)
                .ExecuteNonQueryAsync();
        }

        var migrator = new NpgsqlConnectionStringBuilder(
            admin.ConnectionString)
        {
            Username = "tempodesk_migrator",
            Password = MigratorPassword
        };
        migratorConnectionString = migrator.ConnectionString;
        await using (var connection = new NpgsqlConnection(
            migrator.ConnectionString))
        {
            await connection.OpenAsync();
            var migrations = Path.Combine(
                AppContext.BaseDirectory, "Database", "Migrations");
            foreach (var file in Directory.GetFiles(migrations, "*.sql")
                .OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var sql = (await File.ReadAllTextAsync(file))
                    .TrimStart('\uFEFF');
                await new NpgsqlCommand(sql, connection)
                    .ExecuteNonQueryAsync();
            }
        }

        RuntimeOptions = new DatabaseOptions
        {
            Host = database.Hostname,
            Port = database.GetMappedPublicPort(5432),
            Name = admin.Database!,
            Username = "tempodesk_runtime",
            Password = RuntimePassword,
            CommandTimeoutSeconds = 5
        };
    }

    public TempoDeskIdentityDbContext CreateContext()
    {
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = RuntimeOptions.Host,
            Port = RuntimeOptions.Port,
            Database = RuntimeOptions.Name,
            Username = RuntimeOptions.Username,
            Password = RuntimeOptions.Password,
            GssEncryptionMode = GssEncryptionMode.Disable
        };
        var options = new DbContextOptionsBuilder<TempoDeskIdentityDbContext>()
            .UseNpgsql(connection.ConnectionString)
            .Options;
        return new TempoDeskIdentityDbContext(options);
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(
            migratorConnectionString);
        await connection.OpenAsync();
        await new NpgsqlCommand("""
            TRUNCATE TABLE
                public.task_activity_logs,
                public.task_comments,
                public.task_labels,
                public.labels,
                public.task_assignees,
                public.tasks,
                public.projects,
                public.workspace_invitations,
                public.workspace_members,
                public.workspaces,
                public.security_audit_logs,
                public.password_reset_tokens,
                public.refresh_tokens,
                public.email_verification_tokens,
                public.user_tokens,
                public.external_logins,
                public.user_claims,
                public.users
            RESTART IDENTITY CASCADE
            """, connection).ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await database.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlTestGroup :
    ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}
