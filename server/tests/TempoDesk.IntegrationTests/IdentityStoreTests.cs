using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace TempoDesk.IntegrationTests;

public sealed class IdentityStoreTests
{
    [Fact]
    public async Task IdentityFeaturesWorkAgainstPostgreSqlAsRuntimeRole()
    {
        var postgresPassword = Guid.NewGuid().ToString("N");
        var runtimePassword = Guid.NewGuid().ToString("N");

        await using var database = new PostgreSqlBuilder("postgres:18.6-bookworm")
            .WithDatabase("identity_test")
            .WithUsername("postgres")
            .WithPassword(postgresPassword)
            .Build();

        await database.StartAsync();
        await PrepareDatabaseAsync(database.GetConnectionString(), runtimePassword);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions
        {
            Host = database.Hostname,
            Port = database.GetMappedPublicPort(5432),
            Name = "identity_test",
            Username = "identity_runtime",
            Password = runtimePassword
        }));
        services.AddTempoDeskIdentity();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        const string password = "Correct-Horse-7-Battery!";
        var user = new ApplicationUser { UserName = email, Email = email };

        AssertSucceeded(await users.CreateAsync(user, password));
        Assert.NotEqual(password, user.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(user, password));

        AssertSucceeded(await users.AddClaimAsync(user, new Claim("permission", "test")));
        Assert.Contains(await users.GetClaimsAsync(user), claim =>
            claim.Type == "permission" && claim.Value == "test");

        AssertSucceeded(await users.AddLoginAsync(user,
            new UserLoginInfo("test-provider", "provider-key", "Test Provider")));
        Assert.Contains(await users.GetLoginsAsync(user), login =>
            login.LoginProvider == "test-provider" && login.ProviderKey == "provider-key");

        AssertSucceeded(await users.SetAuthenticationTokenAsync(
            user, "TempoDesk", "session", "opaque-token"));
        Assert.Equal("opaque-token", await users.GetAuthenticationTokenAsync(
            user, "TempoDesk", "session"));

        var emailToken = await users.GenerateEmailConfirmationTokenAsync(user);
        AssertSucceeded(await users.ConfirmEmailAsync(user, emailToken));
        Assert.True(user.EmailConfirmed);

        for (var attempt = 0; attempt < 5; attempt++)
            AssertSucceeded(await users.AccessFailedAsync(user));

        Assert.True(await users.IsLockedOutAsync(user));
        Assert.NotNull(await users.FindByEmailAsync(email));
    }

    private static void AssertSucceeded(IdentityResult result) => Assert.True(
        result.Succeeded,
        string.Join(", ", result.Errors.Select(error => $"{error.Code}: {error.Description}")));

    private static async Task PrepareDatabaseAsync(string connectionString, string runtimePassword)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var setup = $"""
            CREATE ROLE identity_runtime LOGIN PASSWORD '{runtimePassword}';
            GRANT CONNECT ON DATABASE identity_test TO identity_runtime;
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO identity_runtime;
            ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO identity_runtime;
            ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public
                GRANT USAGE, SELECT ON SEQUENCES TO identity_runtime;
            """;
        await ExecuteAsync(connection, setup);

        string[] migrations =
        [
            "202610271944_create_indentity_schema.sql",
            "202610272000_fix_user_claim_id_type.sql"
        ];

        foreach (var migration in migrations)
        {
            var path = Path.Combine(
                AppContext.BaseDirectory, "Database", "Migrations", migration);
            await ExecuteAsync(connection, await File.ReadAllTextAsync(path));
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
