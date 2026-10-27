using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.IntegrationTests;

public sealed class IdentityModelTests
{
    [Fact]
    public void IdentityModelUsesExistingUserOnlySchema()
    {
        var options = new DbContextOptionsBuilder<TempoDeskIdentityDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=tempodesk_test;" +
                "Username=test;Password=test")
            .Options;

        using var context = new TempoDeskIdentityDbContext(options);
        var model = context.Model;

        Assert.Equal(
            "users",
            model.FindEntityType(typeof(ApplicationUser))?.GetTableName());

        Assert.Equal(
            "user_claims",
            model.FindEntityType(typeof(IdentityUserClaim<Guid>))
                ?.GetTableName());

        Assert.Equal(
            "external_logins",
            model.FindEntityType(typeof(IdentityUserLogin<Guid>))
                ?.GetTableName());

        Assert.Equal(
            "user_tokens",
            model.FindEntityType(typeof(IdentityUserToken<Guid>))
                ?.GetTableName());

        Assert.Null(model.FindEntityType(typeof(IdentityRole<Guid>)));

        Assert.DoesNotContain(
            model.GetEntityTypes(),
            entity => entity.ClrType.Name.Contains(
                "Passkey",
                StringComparison.Ordinal));
    }
}
