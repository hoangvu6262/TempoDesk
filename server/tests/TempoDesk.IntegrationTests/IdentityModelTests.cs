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

        var refreshToken = model.FindEntityType(typeof(RefreshToken));
        Assert.Equal("refresh_tokens", refreshToken?.GetTableName());
        Assert.Equal("id", refreshToken?.FindProperty(nameof(RefreshToken.Id))
            ?.GetColumnName());
        Assert.Equal("user_id", refreshToken
            ?.FindProperty(nameof(RefreshToken.UserId))?.GetColumnName());
        Assert.Equal("family_id", refreshToken
            ?.FindProperty(nameof(RefreshToken.FamilyId))?.GetColumnName());
        Assert.Equal("token_hash", refreshToken
            ?.FindProperty(nameof(RefreshToken.TokenHash))?.GetColumnName());
        Assert.Equal("expires_at", refreshToken
            ?.FindProperty(nameof(RefreshToken.ExpiresAt))?.GetColumnName());
        Assert.Equal("used_at", refreshToken
            ?.FindProperty(nameof(RefreshToken.UsedAt))?.GetColumnName());
        Assert.Equal("revoked_at", refreshToken
            ?.FindProperty(nameof(RefreshToken.RevokedAt))?.GetColumnName());
        Assert.Equal("replaced_by_token_id", refreshToken
            ?.FindProperty(nameof(RefreshToken.ReplacedByTokenId))
            ?.GetColumnName());
        Assert.Equal("created_at", refreshToken
            ?.FindProperty(nameof(RefreshToken.CreatedAt))?.GetColumnName());
        Assert.Equal("device_name", refreshToken
            ?.FindProperty(nameof(RefreshToken.DeviceName))?.GetColumnName());
        Assert.Equal("user_agent", refreshToken
            ?.FindProperty(nameof(RefreshToken.UserAgent))?.GetColumnName());
        Assert.Equal("ip_address", refreshToken
            ?.FindProperty(nameof(RefreshToken.IpAddress))?.GetColumnName());

        var resetToken = model.FindEntityType(typeof(PasswordResetToken));
        Assert.Equal("password_reset_tokens", resetToken?.GetTableName());
        Assert.Equal("id", resetToken
            ?.FindProperty(nameof(PasswordResetToken.Id))?.GetColumnName());
        Assert.Equal("user_id", resetToken
            ?.FindProperty(nameof(PasswordResetToken.UserId))?.GetColumnName());
        Assert.Equal("token_hash", resetToken
            ?.FindProperty(nameof(PasswordResetToken.TokenHash))?.GetColumnName());
        Assert.Equal("expires_at", resetToken
            ?.FindProperty(nameof(PasswordResetToken.ExpiresAt))?.GetColumnName());
        Assert.Equal("used_at", resetToken
            ?.FindProperty(nameof(PasswordResetToken.UsedAt))?.GetColumnName());
        Assert.Equal("created_at", resetToken
            ?.FindProperty(nameof(PasswordResetToken.CreatedAt))?.GetColumnName());

        Assert.Null(model.FindEntityType(typeof(IdentityRole<Guid>)));

        Assert.DoesNotContain(
            model.GetEntityTypes(),
            entity => entity.ClrType.Name.Contains(
                "Passkey",
                StringComparison.Ordinal));
    }
}
