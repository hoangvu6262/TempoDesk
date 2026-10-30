using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace TempoDesk.IntegrationTests;

public sealed class LoginIntegrationTests
{
    private const string Password = "Correct-Horse-7-Battery!";
    private const string SigningKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task SuccessfulLoginIssuesAValidMinimalJwt()
    {
        var postgresPassword = Guid.NewGuid().ToString("N");
        var runtimePassword = Guid.NewGuid().ToString("N");
        await using var database = new PostgreSqlBuilder("postgres:18.6-bookworm")
            .WithDatabase("login_test")
            .WithUsername("postgres")
            .WithPassword(postgresPassword)
            .Build();
        await database.StartAsync();
        await PrepareDatabaseAsync(database.GetConnectionString(), runtimePassword);

        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var services = CreateServices(database, runtimePassword, clock);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var login = scope.ServiceProvider
            .GetRequiredService<IAccountLoginService>();
        var user = new ApplicationUser
        {
            UserName = "Person@example.com",
            Email = "Person@example.com",
            EmailConfirmed = true
        };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);

        var result = await login.LoginAsync("PERSON@example.com", Password);

        Assert.True(result.Succeeded);
        Assert.Equal(900, result.ExpiresIn);
        Assert.NotNull(result.AccessToken);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);
        Assert.Equal("https://issuer.example", token.Issuer);
        Assert.Equal("tempodesk-api", Assert.Single(token.Audiences));
        Assert.Equal(user.Id.ToString(), token.Subject);
        Assert.Equal(
            clock.GetUtcNow().AddMinutes(15).ToUnixTimeSeconds(),
            new DateTimeOffset(token.ValidTo).ToUnixTimeSeconds());
        string[] accountClaims =
        [
            JwtRegisteredClaimNames.Sub,
            JwtRegisteredClaimNames.Jti,
            JwtRegisteredClaimNames.Iat
        ];
        Assert.All(accountClaims, claimType =>
            Assert.Contains(token.Claims, claim => claim.Type == claimType));
        string[] allowedClaims =
        [
            .. accountClaims,
            JwtRegisteredClaimNames.Nbf,
            JwtRegisteredClaimNames.Exp,
            JwtRegisteredClaimNames.Iss,
            JwtRegisteredClaimNames.Aud
        ];
        Assert.All(token.Claims, claim => Assert.Contains(
            claim.Type, allowedClaims));
        Assert.DoesNotContain(token.Claims, claim =>
            claim.Type.Contains("email", StringComparison.OrdinalIgnoreCase) ||
            claim.Value == Password);

        var principal = handler.ValidateToken(result.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(SigningKey)),
                ValidateIssuer = true,
                ValidIssuer = "https://issuer.example",
                ValidateAudience = true,
                ValidAudience = "tempodesk-api",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);
        Assert.Equal(user.Id.ToString(), principal.FindFirstValue(
            ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task RejectedLoginsLockOutAndAllAttemptsAreAuditedSafely()
    {
        var postgresPassword = Guid.NewGuid().ToString("N");
        var runtimePassword = Guid.NewGuid().ToString("N");
        await using var database = new PostgreSqlBuilder("postgres:18.6-bookworm")
            .WithDatabase("login_test")
            .WithUsername("postgres")
            .WithPassword(postgresPassword)
            .Build();
        await database.StartAsync();
        await PrepareDatabaseAsync(database.GetConnectionString(), runtimePassword);

        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var services = CreateServices(database, runtimePassword, clock);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var login = scope.ServiceProvider
            .GetRequiredService<IAccountLoginService>();
        var unconfirmed = new ApplicationUser
        {
            UserName = "unconfirmed@example.com",
            Email = "unconfirmed@example.com"
        };
        var confirmed = new ApplicationUser
        {
            UserName = "confirmed@example.com",
            Email = "confirmed@example.com",
            EmailConfirmed = true
        };
        var successful = new ApplicationUser
        {
            UserName = "successful@example.com",
            Email = "successful@example.com",
            EmailConfirmed = true
        };
        Assert.True((await users.CreateAsync(unconfirmed, Password)).Succeeded);
        Assert.True((await users.CreateAsync(confirmed, Password)).Succeeded);
        Assert.True((await users.CreateAsync(successful, Password)).Succeeded);

        Assert.False((await login.LoginAsync(
            "missing@example.com", Password)).Succeeded);
        Assert.False((await login.LoginAsync(
            unconfirmed.Email!, Password)).Succeeded);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.False((await login.LoginAsync(
                confirmed.Email!, "Wrong-Password-7!")).Succeeded);
        }
        Assert.True(await users.IsLockedOutAsync(confirmed));
        Assert.False((await login.LoginAsync(
            confirmed.Email!, Password)).Succeeded);
        var successfulLogin = await login.LoginAsync(
            successful.Email!, Password);
        Assert.True(successfulLogin.Succeeded);

        Assert.Equal(9, await CountAsync(database.GetConnectionString(),
            "SELECT count(*) FROM public.security_audit_logs WHERE event_type = 'user.login'"));
        Assert.Equal(1, await CountAsync(database.GetConnectionString(),
            "SELECT count(*) FROM public.security_audit_logs WHERE event_type = 'user.login' AND succeeded"));
        Assert.Equal(1, await CountAsync(database.GetConnectionString(),
            "SELECT count(*) FROM public.security_audit_logs WHERE event_type = 'user.login' AND user_id IS NULL"));

        await using var connection = new NpgsqlConnection(
            database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT coalesce(string_agg(metadata::text, ' '), '')
            FROM public.security_audit_logs
            WHERE event_type = 'user.login'
            """, connection);
        var metadata = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.DoesNotContain(Password, metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("Wrong-Password-7!", metadata,
            StringComparison.Ordinal);
        Assert.DoesNotContain(successfulLogin.AccessToken!, metadata,
            StringComparison.Ordinal);
    }

    private static ServiceCollection CreateServices(
        PostgreSqlContainer database,
        string runtimePassword,
        TimeProvider timeProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(
            new DatabaseOptions
            {
                Host = database.Hostname,
                Port = database.GetMappedPublicPort(5432),
                Name = "login_test",
                Username = "login_runtime",
                Password = runtimePassword
            }));
        services.AddSingleton<IOptions<AuthenticationOptions>>(Options.Create(
            new AuthenticationOptions
            {
                Enabled = true,
                Issuer = "https://issuer.example",
                Audience = "tempodesk-api",
                SigningKey = SigningKey,
                AccessTokenMinutes = 15
            }));
        services.AddSingleton(timeProvider);
        services.AddTempoDeskIdentity();
        return services;
    }

    private static async Task PrepareDatabaseAsync(
        string connectionString,
        string runtimePassword)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"""
            CREATE ROLE login_runtime LOGIN PASSWORD '{runtimePassword}';
            GRANT CONNECT ON DATABASE login_test TO login_runtime;
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO login_runtime;
            ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO login_runtime;
            ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public
                GRANT USAGE, SELECT ON SEQUENCES TO login_runtime;
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

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(
        string connectionString,
        string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
