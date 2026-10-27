using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TempoDesk.Infrastructure.Identity;

public sealed class TempoDeskIdentityDbContext(
    DbContextOptions<TempoDeskIdentityDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureUsers(builder);
        ConfigureUserClaims(builder);
        ConfigureExternalLogins(builder);
        ConfigureUserTokens(builder);
    }

    private static void ConfigureUsers(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users", "public");

            entity.Property(user => user.Id)
                .HasColumnName("id");

            entity.Property(user => user.UserName)
                .HasColumnName("user_name")
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(user => user.NormalizedUserName)
                .HasColumnName("normalized_user_name")
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(user => user.Email)
                .HasColumnName("email")
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(user => user.NormalizedEmail)
                .HasColumnName("normalized_email")
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(user => user.EmailConfirmed)
                .HasColumnName("email_confirmed");

            entity.Property(user => user.PasswordHash)
                .HasColumnName("password_hash");

            entity.Property(user => user.SecurityStamp)
                .HasColumnName("security_stamp");

            entity.Property(user => user.ConcurrencyStamp)
                .HasColumnName("concurrency_stamp")
                .IsConcurrencyToken();

            entity.Property(user => user.PhoneNumber)
                .HasColumnName("phone_number");

            entity.Property(user => user.PhoneNumberConfirmed)
                .HasColumnName("phone_number_confirmed");

            entity.Property(user => user.TwoFactorEnabled)
                .HasColumnName("two_factor_enabled");

            entity.Property(user => user.LockoutEnd)
                .HasColumnName("lockout_end");

            entity.Property(user => user.LockoutEnabled)
                .HasColumnName("lockout_enabled");

            entity.Property(user => user.AccessFailedCount)
                .HasColumnName("access_failed_count");

            entity.Property(user => user.CreatedAt)
                .HasColumnName("created_at");

            entity.Property(user => user.UpdatedAt)
                .HasColumnName("updated_at");

            entity.HasIndex(user => user.NormalizedUserName)
                .HasDatabaseName("ux_users_normalized_user_name")
                .IsUnique();

            entity.HasIndex(user => user.NormalizedEmail)
                .HasDatabaseName("ux_users_normalized_email")
                .IsUnique();
        });
    }

    private static void ConfigureUserClaims(ModelBuilder builder)
    {
        builder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("user_claims", "public");

            entity.Property(claim => claim.Id)
                .HasColumnName("id");

            entity.Property(claim => claim.UserId)
                .HasColumnName("user_id");

            entity.Property(claim => claim.ClaimType)
                .HasColumnName("claim_type");

            entity.Property(claim => claim.ClaimValue)
                .HasColumnName("claim_value");

            entity.HasIndex(claim => claim.UserId)
                .HasDatabaseName("ix_user_claims_user_id");
        });
    }

    private static void ConfigureExternalLogins(ModelBuilder builder)
    {
        builder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.ToTable("external_logins", "public");

            entity.Property(login => login.LoginProvider)
                .HasColumnName("login_provider")
                .HasMaxLength(128);

            entity.Property(login => login.ProviderKey)
                .HasColumnName("provider_key")
                .HasMaxLength(256);

            entity.Property(login => login.ProviderDisplayName)
                .HasColumnName("provider_display_name");

            entity.Property(login => login.UserId)
                .HasColumnName("user_id");

            entity.HasIndex(login => login.UserId)
                .HasDatabaseName("ix_external_logins_user_id");
        });
    }

    private static void ConfigureUserTokens(ModelBuilder builder)
    {
        builder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("user_tokens", "public");

            entity.Property(token => token.UserId)
                .HasColumnName("user_id");

            entity.Property(token => token.LoginProvider)
                .HasColumnName("login_provider")
                .HasMaxLength(128);

            entity.Property(token => token.Name)
                .HasColumnName("name")
                .HasMaxLength(128);

            entity.Property(token => token.Value)
                .HasColumnName("value");
        });
    }
}
