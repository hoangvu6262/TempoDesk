using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Infrastructure.Identity;

public static class IdentityConfiguration
{
    public static IServiceCollection AddTempoDeskIdentity(
        this IServiceCollection services)
    {
        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<TempoDeskIdentityDbContext>(
            (provider, options) =>
            {
                var database = provider
                    .GetRequiredService<IOptions<DatabaseOptions>>()
                    .Value;

                var connectionString =
                    new NpgsqlConnectionStringBuilder
                    {
                        Host = database.Host,
                        Port = database.Port,
                        Database = database.Name,
                        Username = database.Username,
                        Password = database.Password,
                        Timeout = database.CommandTimeoutSeconds,
                        CommandTimeout =
                            database.CommandTimeoutSeconds,
                        GssEncryptionMode =
                            GssEncryptionMode.Disable
                    }.ConnectionString;

                options.UseNpgsql(connectionString);
            });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Stores.SchemaVersion =
                    IdentitySchemaVersions.Version2;

                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan =
                    TimeSpan.FromMinutes(15);

                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
            })
            .AddEntityFrameworkStores<TempoDeskIdentityDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.TryAddScoped<IEmailVerificationSender,
            DeferredEmailVerificationSender>();
        services.AddScoped<IAccountRegistrationService,
            AccountRegistrationService>();

        return services;
    }
}
