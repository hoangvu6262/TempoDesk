using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Email;

namespace TempoDesk.Infrastructure.Identity;

public static class IdentityConfiguration
{
    public static IServiceCollection AddTempoDeskIdentity(
        this IServiceCollection services)
    {
        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddAuthorization();
        services.AddOptions<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthenticationOptions>>((options, configured) =>
            {
                var settings = configured.Value;
                if (!settings.Enabled)
                    return;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(settings.SigningKey)),
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

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
            SmtpEmailVerificationSender>();
        services.AddScoped<IAccountRegistrationService,
            AccountRegistrationService>();

        return services;
    }
}
