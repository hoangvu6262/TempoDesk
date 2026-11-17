using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.WorkItems.Persistence;
using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.Identity;

public sealed class TempoDeskIdentityDbContext(
    DbContextOptions<TempoDeskIdentityDbContext> options,
    SecurityAuditContext? auditContext = null)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<EmailVerificationToken> EmailVerificationTokens =>
        Set<EmailVerificationToken>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordResetToken> PasswordResetTokens =>
        Set<PasswordResetToken>();

    public DbSet<SecurityAuditLog> SecurityAuditLogs =>
        Set<SecurityAuditLog>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
    public DbSet<WorkspaceInvitation> WorkspaceInvitations =>
        Set<WorkspaceInvitation>();

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
    public DbSet<Label> Labels => Set<Label>();
    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskActivityLog> TaskActivityLogs => Set<TaskActivityLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureUsers(builder);
        ConfigureUserClaims(builder);
        ConfigureExternalLogins(builder);
        ConfigureUserTokens(builder);
        ConfigureRefreshTokens(builder);
        ConfigurePasswordResetTokens(builder);
        ConfigureEmailVerificationTokens(builder);
        ConfigureSecurityAuditLogs(builder);
        WorkspacePersistenceConfiguration.Configure(builder);
        WorkItemPersistenceConfiguration.Configure(builder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnrichAudits();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnrichAudits();
        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnrichAudits()
    {
        if (auditContext is null)
            return;

        foreach (var entry in ChangeTracker.Entries<SecurityAuditLog>()
            .Where(entry => entry.State == EntityState.Added))
        {
            entry.Entity.IpAddress ??= auditContext.IpAddress;
            entry.Entity.UserAgent ??= auditContext.UserAgent;
            entry.Entity.TraceId ??= auditContext.TraceId;
        }
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

    private static void ConfigureEmailVerificationTokens(ModelBuilder builder)
    {
        builder.Entity<EmailVerificationToken>(entity =>
        {
            entity.ToTable("email_verification_tokens", "public");
            entity.HasKey(token => token.Id);
            entity.Property(token => token.Id).HasColumnName("id");
            entity.Property(token => token.UserId).HasColumnName("user_id");
            entity.Property(token => token.TokenHash).HasColumnName("token_hash");
            entity.Property(token => token.ExpiresAt).HasColumnName("expires_at");
            entity.Property(token => token.UsedAt).HasColumnName("used_at");
            entity.Property(token => token.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(token => token.TokenHash)
                .HasDatabaseName("ux_email_verification_tokens_token_hash")
                .IsUnique();
            entity.HasIndex(token => token.UserId)
                .HasDatabaseName("ix_email_verification_tokens_user_id");
            entity.HasIndex(token => token.ExpiresAt)
                .HasDatabaseName("ix_email_verification_tokens_expires_at");
        });
    }

    private static void ConfigureRefreshTokens(ModelBuilder builder)
    {
        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens", "public");
            entity.HasKey(token => token.Id);
            entity.Property(token => token.Id).HasColumnName("id");
            entity.Property(token => token.UserId).HasColumnName("user_id");
            entity.Property(token => token.FamilyId).HasColumnName("family_id");
            entity.Property(token => token.TokenHash).HasColumnName("token_hash");
            entity.Property(token => token.ExpiresAt).HasColumnName("expires_at");
            entity.Property(token => token.UsedAt).HasColumnName("used_at");
            entity.Property(token => token.RevokedAt).HasColumnName("revoked_at");
            entity.Property(token => token.ReplacedByTokenId)
                .HasColumnName("replaced_by_token_id");
            entity.Property(token => token.DeviceName)
                .HasColumnName("device_name")
                .HasMaxLength(200);
            entity.Property(token => token.UserAgent)
                .HasColumnName("user_agent");
            entity.Property(token => token.IpAddress)
                .HasColumnName("ip_address")
                .HasColumnType("inet");
            entity.Property(token => token.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(token => token.TokenHash)
                .HasDatabaseName("ux_refresh_tokens_token_hash")
                .IsUnique();
            entity.HasIndex(token => token.UserId)
                .HasDatabaseName("ix_refresh_tokens_user_id");
            entity.HasIndex(token => token.FamilyId)
                .HasDatabaseName("ix_refresh_tokens_family_id");
            entity.HasIndex(token => token.ExpiresAt)
                .HasDatabaseName("ix_refresh_tokens_expires_at");
        });
    }

    private static void ConfigureSecurityAuditLogs(ModelBuilder builder)
    {
        builder.Entity<SecurityAuditLog>(entity =>
        {
            entity.ToTable("security_audit_logs", "public");
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();
            entity.Property(audit => audit.UserId).HasColumnName("user_id");
            entity.Property(audit => audit.EventType)
                .HasColumnName("event_type")
                .HasMaxLength(100);
            entity.Property(audit => audit.Succeeded).HasColumnName("succeeded");
            entity.Property(audit => audit.IpAddress)
                .HasColumnName("ip_address")
                .HasColumnType("inet");
            entity.Property(audit => audit.UserAgent)
                .HasColumnName("user_agent");
            entity.Property(audit => audit.TraceId)
                .HasColumnName("trace_id")
                .HasMaxLength(100);
            entity.Property(audit => audit.Metadata)
                .HasColumnName("metadata")
                .HasColumnType("jsonb");
            entity.Property(audit => audit.OccurredAt).HasColumnName("occurred_at");
            entity.HasIndex(audit => new { audit.UserId, audit.OccurredAt })
                .HasDatabaseName("ix_security_audit_logs_user_occurred_at")
                .IsDescending(false, true);
            entity.HasIndex(audit => new { audit.EventType, audit.OccurredAt })
                .HasDatabaseName("ix_security_audit_logs_event_occurred_at")
                .IsDescending(false, true);
        });
    }

    private static void ConfigurePasswordResetTokens(ModelBuilder builder)
    {
        builder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("password_reset_tokens", "public");
            entity.HasKey(token => token.Id);
            entity.Property(token => token.Id).HasColumnName("id");
            entity.Property(token => token.UserId).HasColumnName("user_id");
            entity.Property(token => token.TokenHash).HasColumnName("token_hash");
            entity.Property(token => token.ExpiresAt).HasColumnName("expires_at");
            entity.Property(token => token.UsedAt).HasColumnName("used_at");
            entity.Property(token => token.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(token => token.TokenHash)
                .HasDatabaseName("ux_password_reset_tokens_token_hash")
                .IsUnique();
            entity.HasIndex(token => token.UserId)
                .HasDatabaseName("ix_password_reset_tokens_user_id");
            entity.HasIndex(token => token.ExpiresAt)
                .HasDatabaseName("ix_password_reset_tokens_expires_at");
        });
    }
}
