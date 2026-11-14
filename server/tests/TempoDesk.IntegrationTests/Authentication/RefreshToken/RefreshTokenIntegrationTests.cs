using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace TempoDesk.IntegrationTests;

public sealed class RefreshTokenIntegrationTests
{
    private const string Password = "Correct-Horse-7-Battery!";

    [Fact]
    public async Task ValidTokenRotatesOnceAndReturnsNewTokenPair()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var metadata = new SessionMetadata(
            "TempoDesk Test Agent",
            IPAddress.Parse("203.0.113.10"));
        var initial = await environment.LoginAsync(metadata: metadata);
        var clock = environment.Clock;
        clock.Advance(TimeSpan.FromMinutes(1));

        await using var scope = environment.Provider.CreateAsyncScope();
        var tokens = scope.ServiceProvider
            .GetRequiredService<IAuthenticationTokenService>();
        var rotated = await tokens.RefreshAsync(initial.RefreshToken!);

        Assert.NotNull(rotated);
        Assert.NotEqual(initial.AccessToken, rotated.AccessToken);
        Assert.NotEqual(initial.RefreshToken, rotated.RefreshToken);
        Assert.Equal(clock.GetUtcNow().AddDays(30),
            rotated.RefreshTokenExpiresAt);

        await using var connection = new NpgsqlConnection(
            environment.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT used_at, revoked_at, replaced_by_token_id,
                   family_id, token_hash, user_agent, ip_address, device_name
            FROM public.refresh_tokens
            ORDER BY created_at
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(clock.GetUtcNow(),
            reader.GetFieldValue<DateTimeOffset>(0),
            TimeSpan.FromMicroseconds(1));
        Assert.True(reader.IsDBNull(1));
        var replacementId = reader.GetGuid(2);
        var familyId = reader.GetGuid(3);
        Assert.Equal("TempoDesk Test Agent", reader.GetString(5));
        Assert.Equal(metadata.IpAddress, reader.GetFieldValue<IPAddress>(6));
        Assert.True(reader.IsDBNull(7));
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(familyId, reader.GetGuid(3));
        Assert.Equal(64, reader.GetString(4).Length);
        Assert.Equal("TempoDesk Test Agent", reader.GetString(5));
        Assert.Equal(metadata.IpAddress, reader.GetFieldValue<IPAddress>(6));
        Assert.True(reader.IsDBNull(7));
        Assert.False(await reader.ReadAsync());
        await reader.CloseAsync();

        command.CommandText = """
            SELECT count(*)
            FROM public.refresh_tokens
            WHERE id = @replacement_id
            """;
        command.Parameters.AddWithValue("replacement_id", replacementId);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task MissingMalformedExpiredAndRevokedTokensAreRejected()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        Assert.Null(await environment.RefreshAsync(""));
        Assert.Null(await environment.RefreshAsync(new string('x', 513)));
        Assert.Null(await environment.RefreshAsync(new string('A', 43)));

        var expired = await environment.LoginAsync("expired");
        environment.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Null(await environment.RefreshAsync(expired.RefreshToken!));

        var revoked = await environment.LoginAsync("revoked");
        await environment.ExecuteAsync("""
            UPDATE public.refresh_tokens
            SET revoked_at = CURRENT_TIMESTAMP
            WHERE used_at IS NULL AND revoked_at IS NULL
            """);
        Assert.Null(await environment.RefreshAsync(revoked.RefreshToken!));

        Assert.Equal(5L, await environment.CountAsync("""
            SELECT count(*)
            FROM public.security_audit_logs
            WHERE event_type = 'token.refresh' AND NOT succeeded
            """));
    }

    [Fact]
    public async Task ReusingRotatedTokenRevokesTheWholeFamily()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var initial = await environment.LoginAsync();
        var rotated = await environment.RefreshAsync(initial.RefreshToken!);
        Assert.NotNull(rotated);

        Assert.Null(await environment.RefreshAsync(initial.RefreshToken!));
        Assert.Null(await environment.RefreshAsync(rotated.RefreshToken));
        Assert.Equal(0L, await environment.CountAsync("""
            SELECT count(*)
            FROM public.refresh_tokens
            WHERE revoked_at IS NULL
            """));
        Assert.Equal(1L, await environment.CountAsync("""
            SELECT count(*)
            FROM public.security_audit_logs
            WHERE event_type = 'token.refresh.reuse'
            """));
    }

    [Fact]
    public async Task ConcurrentRefreshAllowsOneRotationThenRevokesFamily()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var initial = await environment.LoginAsync();

        var first = environment.RefreshAsync(initial.RefreshToken!);
        var second = environment.RefreshAsync(initial.RefreshToken!);
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result is not null);
        Assert.Equal(0L, await environment.CountAsync("""
            SELECT count(*)
            FROM public.refresh_tokens
            WHERE revoked_at IS NULL
            """));
        Assert.Equal(2L, await environment.CountAsync("""
            SELECT count(*)
            FROM public.refresh_tokens
            """));
        Assert.Equal(1L, await environment.CountAsync("""
            SELECT count(*)
            FROM public.security_audit_logs
            WHERE event_type = 'token.refresh.reuse'
            """));
    }

    [Fact]
    public async Task SessionListAndSingleRevokeEnforceOwnershipAndIdempotency()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var owner = await environment.LoginWithUserAsync(
            "owner",
            new SessionMetadata("First Agent", IPAddress.Parse("203.0.113.1")));
        environment.Clock.Advance(TimeSpan.FromMinutes(1));
        await environment.LoginExistingAsync(
            "owner@example.com",
            new SessionMetadata("Second Agent", IPAddress.Parse("203.0.113.2")));
        var other = await environment.LoginWithUserAsync("other");

        await using var scope = environment.Provider.CreateAsyncScope();
        var sessions = scope.ServiceProvider
            .GetRequiredService<IAuthenticationSessionService>();
        var listed = await sessions.ListAsync(owner.UserId);

        Assert.Equal(2, listed.Count);
        Assert.Equal("Second Agent", listed[0].UserAgent);
        Assert.All(listed, session =>
            Assert.Equal(AuthenticationSessionStatus.Active, session.Status));
        Assert.DoesNotContain(listed, session => session.UserAgent is null);

        var revoked = listed.Single(session =>
            session.UserAgent == "First Agent");
        Assert.True(await sessions.RevokeAsync(owner.UserId, revoked.Id));
        var revokedAt = await environment.GetRevokedAtAsync(revoked.Id);
        environment.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await sessions.RevokeAsync(owner.UserId, revoked.Id));
        Assert.Equal(revokedAt, await environment.GetRevokedAtAsync(revoked.Id));
        Assert.False(await sessions.RevokeAsync(other.UserId, revoked.Id));

        listed = await sessions.ListAsync(owner.UserId);
        Assert.Equal(AuthenticationSessionStatus.Revoked,
            listed.Single(session => session.Id == revoked.Id).Status);
        Assert.Equal(AuthenticationSessionStatus.Active,
            listed.Single(session => session.Id != revoked.Id).Status);
    }

    [Fact]
    public async Task SessionListReportsExpiredFamily()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var login = await environment.LoginWithUserAsync("expired-session");
        environment.Clock.Advance(TimeSpan.FromDays(31));

        await using var scope = environment.Provider.CreateAsyncScope();
        var sessions = scope.ServiceProvider
            .GetRequiredService<IAuthenticationSessionService>();
        var session = Assert.Single(await sessions.ListAsync(login.UserId));

        Assert.Equal(AuthenticationSessionStatus.Expired, session.Status);
    }

    [Fact]
    public async Task SessionLogoutAndRevokeAllAreScopedAndAudited()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var owner = await environment.LoginWithUserAsync("logout-owner");
        var second = await environment.LoginExistingAsync(
            "logout-owner@example.com");
        var other = await environment.LoginWithUserAsync("logout-other");

        await using var scope = environment.Provider.CreateAsyncScope();
        var sessions = scope.ServiceProvider
            .GetRequiredService<IAuthenticationSessionService>();
        await sessions.LogoutAsync(owner.Login.RefreshToken!);
        await sessions.LogoutAsync(owner.Login.RefreshToken!);
        await sessions.LogoutAsync("unknown-token");

        var ownerSessions = await sessions.ListAsync(owner.UserId);
        Assert.Equal(2, ownerSessions.Count);
        Assert.Single(ownerSessions, session =>
            session.Status == AuthenticationSessionStatus.Active);
        Assert.Single(ownerSessions, session =>
            session.Status == AuthenticationSessionStatus.Revoked);

        await sessions.RevokeAllAsync(owner.UserId);
        await sessions.RevokeAllAsync(owner.UserId);
        Assert.All(await sessions.ListAsync(owner.UserId), session =>
            Assert.Equal(AuthenticationSessionStatus.Revoked, session.Status));
        Assert.Equal(AuthenticationSessionStatus.Active,
            Assert.Single(await sessions.ListAsync(other.UserId)).Status);
        Assert.Null(await environment.RefreshAsync(second.RefreshToken!));

        Assert.Equal(3L, await environment.CountAsync("""
            SELECT count(*) FROM public.security_audit_logs
            WHERE event_type = 'auth.logout'
            """));
        Assert.Equal(2L, await environment.CountAsync("""
            SELECT count(*) FROM public.security_audit_logs
            WHERE event_type = 'session.revoke_all' AND succeeded
            """));
    }

    private sealed class TestEnvironment : IAsyncDisposable
    {
        private TestEnvironment(
            PostgreSqlContainer database,
            ServiceProvider provider,
            ManualTimeProvider clock)
        {
            Database = database;
            Provider = provider;
            Clock = clock;
        }

        public PostgreSqlContainer Database { get; }
        public ServiceProvider Provider { get; }
        public ManualTimeProvider Clock { get; }

        public static async Task<TestEnvironment> CreateAsync()
        {
            var postgresPassword = Guid.NewGuid().ToString("N");
            var runtimePassword = Guid.NewGuid().ToString("N");
            var database = new PostgreSqlBuilder("postgres:18.6-bookworm")
                .WithDatabase("refresh_token_test")
                .WithUsername("postgres")
                .WithPassword(postgresPassword)
                .Build();
            await database.StartAsync();
            await PrepareDatabaseAsync(
                database.GetConnectionString(), runtimePassword);

            var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(
                new DatabaseOptions
                {
                    Host = database.Hostname,
                    Port = database.GetMappedPublicPort(5432),
                    Name = "refresh_token_test",
                    Username = "refresh_runtime",
                    Password = runtimePassword
                }));
            services.AddSingleton<IOptions<AuthenticationOptions>>(
                Options.Create(new AuthenticationOptions
                {
                    Enabled = true,
                    Issuer = "https://issuer.example",
                    Audience = "tempodesk-api",
                    SigningKey =
                        "0123456789abcdef0123456789abcdef",
                    AccessTokenMinutes = 15,
                    RefreshTokenDays = 30
                }));
            services.AddSingleton<TimeProvider>(clock);
            services.AddTempoDeskIdentity();
            return new TestEnvironment(
                database, services.BuildServiceProvider(), clock);
        }

        public async Task<LoginResult> LoginAsync(
            string name = "person",
            SessionMetadata? metadata = null)
        {
            var result = await LoginWithUserAsync(name, metadata);
            return result.Login;
        }

        public async Task<(Guid UserId, LoginResult Login)> LoginWithUserAsync(
            string name,
            SessionMetadata? metadata = null)
        {
            await using var scope = Provider.CreateAsyncScope();
            var users = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = $"{name}@example.com",
                Email = $"{name}@example.com",
                EmailConfirmed = true
            };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            var login = await scope.ServiceProvider
                .GetRequiredService<IAccountLoginService>()
                .LoginAsync(user.Email, Password, metadata);
            return (user.Id, login);
        }

        public async Task<LoginResult> LoginExistingAsync(
            string email,
            SessionMetadata? metadata = null)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<IAccountLoginService>()
                .LoginAsync(email, Password, metadata);
        }

        public async Task<AuthenticationTokenResult?> RefreshAsync(
            string rawToken)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<IAuthenticationTokenService>()
                .RefreshAsync(rawToken);
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(
                Database.GetConnectionString());
            await connection.OpenAsync();
            await ExecuteAsync(connection, sql);
        }

        public async Task<long> CountAsync(string sql)
        {
            await using var connection = new NpgsqlConnection(
                Database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return (long)(await command.ExecuteScalarAsync())!;
        }

        public async Task<DateTimeOffset> GetRevokedAtAsync(Guid familyId)
        {
            await using var connection = new NpgsqlConnection(
                Database.GetConnectionString());
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT min(revoked_at)
                FROM public.refresh_tokens
                WHERE family_id = @family_id
                """, connection);
            command.Parameters.AddWithValue("family_id", familyId);
            var value = await command.ExecuteScalarAsync();
            return value switch
            {
                DateTimeOffset timestamp => timestamp,
                DateTime timestamp => new DateTimeOffset(
                    DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
                _ => throw new InvalidOperationException(
                    "Expected a session revocation timestamp.")
            };
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
                GRANT CONNECT ON DATABASE refresh_token_test TO refresh_runtime;
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
                await ExecuteAsync(
                    connection, await File.ReadAllTextAsync(path));
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

    public sealed class ManualTimeProvider(
        DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
