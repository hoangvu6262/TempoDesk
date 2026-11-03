using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Identity;
using Testcontainers.PostgreSql;

namespace TempoDesk.IntegrationTests;

public sealed class RegistrationIntegrationTests
{
    [Fact]
    public async Task VerificationSenderContractCarriesTheRawTokenOnlyForDelivery()
    {
        IEmailVerificationSender sender = new CapturingVerificationSender();
        var message = new VerificationEmail(
            "person@example.com",
            "raw-token",
            DateTimeOffset.UtcNow.AddMinutes(30));

        Assert.True(await sender.SendAsync(message, CancellationToken.None));
        Assert.Equal(message, ((CapturingVerificationSender)sender).LastMessage);
    }

    [Fact]
    public async Task RegisterCreatesUnconfirmedUserAndStoresOnlyTokenHash()
    {
        var postgresPassword = Guid.NewGuid().ToString("N");
        var runtimePassword = Guid.NewGuid().ToString("N");

        await using var database = new PostgreSqlBuilder("postgres:18.6-bookworm")
            .WithDatabase("registration_test")
            .WithUsername("postgres")
            .WithPassword(postgresPassword)
            .Build();

        await database.StartAsync();
        await PrepareDatabaseAsync(database.GetConnectionString(), runtimePassword);

        var sender = new CapturingVerificationSender();
        var services = CreateServices(database, runtimePassword, sender);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var registration = scope.ServiceProvider
            .GetRequiredService<IAccountRegistrationService>();
        var users = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        const string email = "Person@Example.com";
        const string password = "Correct-Horse-7-Battery!";

        var result = await registration.RegisterAsync(email, password);

        Assert.True(result.Succeeded);
        var message = Assert.Single(sender.Messages);
        Assert.Equal(email, message.Email);
        Assert.False(string.IsNullOrWhiteSpace(message.Token));
        Assert.InRange(
            message.ExpiresAt - DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(55),
            TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(5));

        var user = await users.FindByEmailAsync("person@example.com");
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
        Assert.True(await users.CheckPasswordAsync(user, password));

        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT token_hash FROM public.email_verification_tokens", connection);
        var storedHash = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.Equal(64, storedHash.Length);
        Assert.NotEqual(message.Token, storedHash);

        var defaultServices = CreateServices(database, runtimePassword);
        await using var defaultProvider = defaultServices.BuildServiceProvider();
        await using var defaultScope = defaultProvider.CreateAsyncScope();
        var deferredRegistration = defaultScope.ServiceProvider
            .GetRequiredService<IAccountRegistrationService>();
        Assert.True((await deferredRegistration.RegisterAsync(
            "deferred@example.com", password)).Succeeded);
        Assert.Equal(1, await CountAsync(database.GetConnectionString(), """
            SELECT count(*)
            FROM public.security_audit_logs audit
            JOIN public.users users ON users.id = audit.user_id
            WHERE users.normalized_email = 'DEFERRED@EXAMPLE.COM'
              AND audit.event_type = 'email.verification.delivery'
              AND audit.succeeded = false
            """));
    }

    [Fact]
    public async Task VerificationTokensAreSingleUseExpiringAndReplacedOnResend()
    {
        var postgresPassword = Guid.NewGuid().ToString("N");
        var runtimePassword = Guid.NewGuid().ToString("N");

        await using var database = new PostgreSqlBuilder("postgres:18.6-bookworm")
            .WithDatabase("registration_test")
            .WithUsername("postgres")
            .WithPassword(postgresPassword)
            .Build();

        await database.StartAsync();
        await PrepareDatabaseAsync(database.GetConnectionString(), runtimePassword);

        var sender = new CapturingVerificationSender();
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
        var services = CreateServices(database, runtimePassword, sender, clock);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var registration = scope.ServiceProvider
            .GetRequiredService<IAccountRegistrationService>();
        var users = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider
            .GetRequiredService<SignInManager<ApplicationUser>>();
        const string password = "Correct-Horse-7-Battery!";

        Assert.True((await registration.RegisterAsync(
            "Lifecycle@Example.com", password)).Succeeded);
        var lifecycleToken = sender.Messages[^1].Token;
        var lifecycleUser = await users.FindByEmailAsync("lifecycle@example.com");
        Assert.NotNull(lifecycleUser);
        Assert.False(await signIn.CanSignInAsync(lifecycleUser));
        Assert.True(await registration.VerifyEmailAsync(lifecycleToken));
        Assert.False(await registration.VerifyEmailAsync(lifecycleToken));
        Assert.True((await users.FindByEmailAsync("lifecycle@example.com"))!.EmailConfirmed);

        Assert.True((await registration.RegisterAsync(
            "LIFECYCLE@example.com", password)).Succeeded);
        Assert.Equal(1, await CountAsync(database.GetConnectionString(),
            "SELECT count(*) FROM public.users WHERE normalized_email = 'LIFECYCLE@EXAMPLE.COM'"));

        Assert.True((await registration.RegisterAsync(
            "expired@example.com", password)).Succeeded);
        var expiredToken = sender.Messages[^1].Token;
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.False(await registration.VerifyEmailAsync(expiredToken));

        Assert.True((await registration.RegisterAsync(
            "resend@example.com", password)).Succeeded);
        var originalToken = sender.Messages[^1].Token;
        await registration.ResendVerificationAsync("RESEND@example.com");
        var replacementToken = sender.Messages[^1].Token;
        Assert.NotEqual(originalToken, replacementToken);
        Assert.False(await registration.VerifyEmailAsync(originalToken));
        Assert.True(await registration.VerifyEmailAsync(replacementToken));

        Assert.True((await registration.RegisterAsync(
            "concurrent@example.com", password)).Succeeded);
        await using var firstResendScope = provider.CreateAsyncScope();
        await using var secondResendScope = provider.CreateAsyncScope();
        await Task.WhenAll(
            firstResendScope.ServiceProvider
                .GetRequiredService<IAccountRegistrationService>()
                .ResendVerificationAsync("concurrent@example.com"),
            secondResendScope.ServiceProvider
                .GetRequiredService<IAccountRegistrationService>()
                .ResendVerificationAsync("CONCURRENT@example.com"));
        Assert.Equal(1, await CountAsync(database.GetConnectionString(), """
            SELECT count(*)
            FROM public.email_verification_tokens token
            JOIN public.users users ON users.id = token.user_id
            WHERE users.normalized_email = 'CONCURRENT@EXAMPLE.COM'
              AND token.used_at IS NULL
            """));

        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT string_agg(metadata::text, ' ') FROM public.security_audit_logs",
            connection);
        var metadata = Assert.IsType<string>(await command.ExecuteScalarAsync());
        Assert.DoesNotContain(password, metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(lifecycleToken, metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(originalToken, metadata, StringComparison.Ordinal);
        Assert.DoesNotContain(replacementToken, metadata, StringComparison.Ordinal);
    }

    private static ServiceCollection CreateServices(
        PostgreSqlContainer database,
        string runtimePassword,
        CapturingVerificationSender? sender = null,
        TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication();
        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions
        {
            Host = database.Hostname,
            Port = database.GetMappedPublicPort(5432),
            Name = "registration_test",
            Username = "registration_runtime",
            Password = runtimePassword
        }));
        services.AddTempoDeskIdentity();
        if (sender is not null)
        {
            services.RemoveAll<IEmailVerificationSender>();
            services.AddSingleton<IEmailVerificationSender>(sender);
        }
        if (timeProvider is not null)
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(timeProvider);
        }
        return services;
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

    private static async Task PrepareDatabaseAsync(
        string connectionString,
        string runtimePassword)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var setup = $"""
            CREATE ROLE registration_runtime LOGIN PASSWORD '{runtimePassword}';
            GRANT CONNECT ON DATABASE registration_test TO registration_runtime;
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT USAGE ON SCHEMA public TO registration_runtime;
            ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO registration_runtime;
            ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public
                GRANT USAGE, SELECT ON SEQUENCES TO registration_runtime;
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

    private sealed class CapturingVerificationSender : IEmailVerificationSender
    {
        public List<VerificationEmail> Messages { get; } = [];
        public VerificationEmail? LastMessage => Messages.LastOrDefault();

        public Task<bool> SendAsync(
            VerificationEmail message,
            CancellationToken cancellationToken)
        {
            lock (Messages)
                Messages.Add(message);
            return Task.FromResult(true);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }
}
