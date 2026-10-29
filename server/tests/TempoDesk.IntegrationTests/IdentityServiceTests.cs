using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.IntegrationTests;

public sealed class IdentityServiceTests
{
    [Fact]
    public void IdentityServicesUseRequiredSecurityPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTempoDeskIdentity();

        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptions<IdentityOptions>>()
            .Value;

        Assert.Equal(12, options.Password.RequiredLength);
        Assert.Equal(4, options.Password.RequiredUniqueChars);
        Assert.True(options.Password.RequireDigit);
        Assert.True(options.Password.RequireLowercase);
        Assert.True(options.Password.RequireUppercase);

        Assert.True(options.Lockout.AllowedForNewUsers);
        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15),
            options.Lockout.DefaultLockoutTimeSpan);

        Assert.True(options.User.RequireUniqueEmail);
        Assert.True(options.SignIn.RequireConfirmedEmail);

        Assert.NotNull(
            provider.GetService<IPasswordHasher<ApplicationUser>>());

        Assert.Contains(
            TokenOptions.DefaultProvider,
            options.Tokens.ProviderMap);

        Assert.Contains(
            TokenOptions.DefaultEmailProvider,
            options.Tokens.ProviderMap);
    }
}
