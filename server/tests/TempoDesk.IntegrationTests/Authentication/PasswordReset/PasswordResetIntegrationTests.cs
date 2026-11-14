using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace TempoDesk.IntegrationTests;

public sealed class PasswordResetIntegrationTests
{
    private const string OldPassword = "Correct-Horse-7-Battery!";
    private const string NewPassword = "New-Correct-8-Battery!";

    [Fact]
    public async Task ForgotPasswordIsNeutralAndReplacesUnusedToken()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var user = await environment.CreateUserAsync();
        var resets = environment.Resets();

        await resets.ForgotPasswordAsync(user.Email!);
        var first = Assert.Single(environment.Sender.Messages).Token;
        environment.Clock.Advance(TimeSpan.FromMinutes(1));
        await resets.ForgotPasswordAsync("missing@example.com");
        await resets.ForgotPasswordAsync(user.Email!);
        var second = environment.Sender.Messages[1].Token;

        Assert.NotEqual(first, second);
        Assert.Equal(2L, await environment.CountAsync("""
            SELECT count(*) FROM public.password_reset_tokens
            """));
        Assert.Equal(1L, await environment.CountAsync("""
            SELECT count(*) FROM public.password_reset_tokens
            WHERE used_at IS NULL
            """));
        Assert.Equal(0L, await environment.CountAsync($"""
            SELECT count(*) FROM public.password_reset_tokens
            WHERE token_hash IN ('{first}', '{second}')
            """));
        Assert.Equal(3L, await environment.CountAsync("""
            SELECT count(*) FROM public.security_audit_logs
            WHERE event_type = 'password.reset.requested'
            """));
    }

    [Fact]
    public async Task ResetChangesPasswordConsumesTokenAndRevokesSessions()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var user = await environment.CreateUserAsync();
        var login = await environment.LoginAsync(user.Email!, OldPassword);
        var resets = environment.Resets();
        await resets.ForgotPasswordAsync(user.Email!);
        var token = Assert.Single(environment.Sender.Messages).Token;

        var weak = await resets.ResetPasswordAsync(token, "weak");
        Assert.False(weak.Succeeded);
        Assert.False(weak.TokenRejected);

        var result = await resets.ResetPasswordAsync(token, NewPassword);

        Assert.True(result.Succeeded);
        Assert.False((await environment.LoginAsync(
            user.Email!, OldPassword)).Succeeded);
        Assert.True((await environment.LoginAsync(
            user.Email!, NewPassword)).Succeeded);
        Assert.Null(await environment.RefreshAsync(login.RefreshToken!));
        Assert.True((await resets.ResetPasswordAsync(
            token, NewPassword)).TokenRejected);
    }

    [Fact]
    public async Task ExpiredAndUnknownTokensAreRejected()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var user = await environment.CreateUserAsync();
        var resets = environment.Resets();
        await resets.ForgotPasswordAsync(user.Email!);
        var token = Assert.Single(environment.Sender.Messages).Token;
        environment.Clock.Advance(TimeSpan.FromMinutes(31));

        Assert.True((await resets.ResetPasswordAsync(
            token, NewPassword)).TokenRejected);
        Assert.True((await resets.ResetPasswordAsync(
            "unknown-token", NewPassword)).TokenRejected);
    }

    private sealed class TestEnvironment : IAsyncDisposable
    {
        private TestEnvironment(
            PostgreSqlContainer database,
            ServiceProvider provider,
            ManualTimeProvider clock,
            RecordingSender sender)
        {
            Database = database;
            Provider = provider;
            Clock = clock;
            Sender = sender;
        }

        public PostgreSqlContainer Database { get; }
        public ServiceProvider Provider { get; }
        public ManualTimeProvider Clock { get; }
        public RecordingSender Sender { get; }

        public static async Task<TestEnvironment> CreateAsync()
        {
            var postgresPassword = Guid.NewGuid().ToString("N");
            var runtimePassword = Guid.NewGuid().ToString("N");
            var database = new PostgreSqlBuilder("postgres:18.6-bookworm")
                .WithDatabase("password_reset_test")
                .WithUsername("postgres")
                .WithPassword(postgresPassword)
                .Build();
            await database.StartAsync();
            await PrepareDatabaseAsync(
                database.GetConnectionString(), runtimePassword);

            var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            var sender = new RecordingSender();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(
                new DatabaseOptions
                {
                    Host = database.Hostname,
                    Port = database.GetMappedPublicPort(5432),
                    Name = "password_reset_test",
                    Username = "refresh_runtime",
                    Password = runtimePassword
                }));
            services.AddSingleton<IOptions<AuthenticationOptions>>(
                Options.Create(new AuthenticationOptions
                {
                    Enabled = true,
                    Issuer = "https://issuer.example",
                    Audience = "tempodesk-api",
                    SigningKey = "0123456789abcdef0123456789abcdef",
                    AccessTokenMinutes = 15,
                    RefreshTokenDays = 30
                }));
            services.AddSingleton<TimeProvider>(clock);
            services.AddTempoDeskIdentity();
            services.RemoveAll<IPasswordResetEmailSender>();
            services.AddSingleton<IPasswordResetEmailSender>(sender);
            return new TestEnvironment(
                database, services.BuildServiceProvider(), clock, sender);
        }

        public IAccountPasswordResetService Resets() => Provider
            .GetRequiredService<IAccountPasswordResetService>();

        public async Task<ApplicationUser> CreateUserAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var user = new ApplicationUser
            {
                UserName = "person@example.com",
                Email = "person@example.com",
                EmailConfirmed = true
            };
            Assert.True((await scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>()
                .CreateAsync(user, OldPassword)).Succeeded);
            return user;
        }

        public async Task<LoginResult> LoginAsync(
            string email,
            string password)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<IAccountLoginService>()
                .LoginAsync(email, password);
        }

        public async Task<AuthenticationTokenResult?> RefreshAsync(string token)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<IAuthenticationTokenService>()
                .RefreshAsync(token);
        }

        public async Task<long> CountAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(
                Database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            await Database.DisposeAsync();
        }

        private static async Task PrepareDatabaseAsync(
            string connectionString,
            string runtimePassword)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"""
                CREATE ROLE refresh_runtime LOGIN PASSWORD '{runtimePassword}';
                GRANT CONNECT ON DATABASE password_reset_test TO refresh_runtime;
                REVOKE ALL ON SCHEMA public FROM PUBLIC;
                GRANT USAGE ON SCHEMA public TO refresh_runtime;
                ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO refresh_runtime;
                ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES TO refresh_runtime;
                """);
            foreach (var migration in new[]
            {
                "202610271944_create_indentity_schema.sql",
                "202610272000_fix_user_claim_id_type.sql"
            })
            {
                var path = Path.Combine(AppContext.BaseDirectory,
                    "Database", "Migrations", migration);
                await ExecuteAsync(connection, await File.ReadAllTextAsync(path));
            }
        }

        private static async Task ExecuteAsync(
            NpgsqlConnection connection,
            string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class RecordingSender : IPasswordResetEmailSender
    {
        public List<PasswordResetEmail> Messages { get; } = [];

        public Task<bool> SendAsync(
            PasswordResetEmail message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult(true);
        }
    }

    private sealed class ManualTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }
}
