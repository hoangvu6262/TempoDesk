using Microsoft.EntityFrameworkCore;

namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public static class WorkspacePersistenceConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        ConfigureWorkspaces(builder);
        ConfigureMembers(builder);
        ConfigureInvitations(builder);
    }

    private static void ConfigureWorkspaces(ModelBuilder builder)
    {
        builder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_workspaces_name_not_empty", "btrim(name) <> ''");
                table.HasCheckConstraint(
                    "ck_workspaces_type",
                    "type IN ('personal', 'team')");
                table.HasCheckConstraint(
                    "ck_workspaces_updated_at", "updated_at >= created_at");
            });
            entity.HasKey(workspace => workspace.Id);
            entity.Property(workspace => workspace.Id).HasColumnName("id");
            entity.Property(workspace => workspace.Name)
                .HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(workspace => workspace.Type)
                .HasColumnName("type").HasMaxLength(20).IsRequired();
            entity.Property(workspace => workspace.OwnerUserId)
                .HasColumnName("owner_user_id");
            entity.Property(workspace => workspace.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(workspace => workspace.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(workspace => workspace.Owner)
                .WithMany()
                .HasForeignKey(workspace => workspace.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspaces_owner");
            entity.HasIndex(
                    workspace => workspace.OwnerUserId,
                    "IX_Workspaces_OwnerUserId")
                .HasDatabaseName("ix_workspaces_owner_user_id");
            entity.HasIndex(
                    workspace => workspace.OwnerUserId,
                    "UX_Workspaces_PersonalOwner")
                .HasDatabaseName("ux_workspaces_personal_owner")
                .HasFilter("type = 'personal'")
                .IsUnique();
        });
    }

    private static void ConfigureMembers(ModelBuilder builder)
    {
        builder.Entity<WorkspaceMember>(entity =>
        {
            entity.ToTable("workspace_members", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_workspace_members_role",
                    "role IN ('owner', 'admin', 'member', 'viewer')");
                table.HasCheckConstraint(
                    "ck_workspace_members_removed_at",
                    "removed_at IS NULL OR removed_at >= created_at");
            });
            entity.HasKey(member => member.Id);
            entity.Property(member => member.Id).HasColumnName("id");
            entity.Property(member => member.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(member => member.UserId).HasColumnName("user_id");
            entity.Property(member => member.Role)
                .HasColumnName("role").HasMaxLength(20).IsRequired();
            entity.Property(member => member.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(member => member.RemovedAt)
                .HasColumnName("removed_at");
            entity.HasOne(member => member.Workspace)
                .WithMany(workspace => workspace.Members)
                .HasForeignKey(member => member.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspace_members_workspace");
            entity.HasOne(member => member.User)
                .WithMany()
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspace_members_user");
            entity.HasIndex(member => new
                { member.WorkspaceId, member.UserId })
                .HasDatabaseName("ux_workspace_members_active_user")
                .HasFilter("removed_at IS NULL")
                .IsUnique();
            entity.HasIndex(
                    member => member.WorkspaceId,
                    "UX_WorkspaceMembers_ActiveOwner")
                .HasDatabaseName("ux_workspace_members_active_owner")
                .HasFilter("role = 'owner' AND removed_at IS NULL")
                .IsUnique();
            entity.HasIndex(
                    member => member.WorkspaceId,
                    "IX_WorkspaceMembers_WorkspaceActive")
                .HasDatabaseName("ix_workspace_members_workspace_active")
                .HasFilter("removed_at IS NULL");
            entity.HasIndex(member => member.UserId)
                .HasDatabaseName("ix_workspace_members_user_active")
                .HasFilter("removed_at IS NULL");
        });
    }

    private static void ConfigureInvitations(ModelBuilder builder)
    {
        builder.Entity<WorkspaceInvitation>(entity =>
        {
            entity.ToTable("workspace_invitations", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_workspace_invitations_email_not_empty",
                    "btrim(email) <> ''");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_normalized_email_not_empty",
                    "btrim(normalized_email) <> ''");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_role",
                    "role IN ('admin', 'member', 'viewer')");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_token_hash",
                    "token_hash ~ '^[0-9a-f]{64}$'");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_expiry",
                    "expires_at > created_at");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_accepted_at",
                    "accepted_at IS NULL OR accepted_at >= created_at");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_cancelled_at",
                    "cancelled_at IS NULL OR cancelled_at >= created_at");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_single_outcome",
                    "accepted_at IS NULL OR cancelled_at IS NULL");
            });
            entity.HasKey(invitation => invitation.Id);
            entity.Property(invitation => invitation.Id).HasColumnName("id");
            entity.Property(invitation => invitation.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(invitation => invitation.InvitedByUserId)
                .HasColumnName("invited_by_user_id");
            entity.Property(invitation => invitation.Email)
                .HasColumnName("email").HasMaxLength(256).IsRequired();
            entity.Property(invitation => invitation.NormalizedEmail)
                .HasColumnName("normalized_email")
                .HasMaxLength(256).IsRequired();
            entity.Property(invitation => invitation.Role)
                .HasColumnName("role").HasMaxLength(20).IsRequired();
            entity.Property(invitation => invitation.TokenHash)
                .HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.Property(invitation => invitation.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(invitation => invitation.ExpiresAt)
                .HasColumnName("expires_at");
            entity.Property(invitation => invitation.AcceptedAt)
                .HasColumnName("accepted_at");
            entity.Property(invitation => invitation.CancelledAt)
                .HasColumnName("cancelled_at");
            entity.HasOne(invitation => invitation.Workspace)
                .WithMany(workspace => workspace.Invitations)
                .HasForeignKey(invitation => invitation.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspace_invitations_workspace");
            entity.HasOne(invitation => invitation.InvitedByUser)
                .WithMany()
                .HasForeignKey(invitation => invitation.InvitedByUserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspace_invitations_inviter");
            entity.HasIndex(invitation => invitation.TokenHash)
                .HasDatabaseName("ux_workspace_invitations_token_hash")
                .IsUnique();
            entity.HasIndex(invitation => new
                { invitation.WorkspaceId, invitation.NormalizedEmail })
                .HasDatabaseName("ux_workspace_invitations_active_email")
                .HasFilter("accepted_at IS NULL AND cancelled_at IS NULL")
                .IsUnique();
            entity.HasIndex(invitation => invitation.WorkspaceId)
                .HasDatabaseName("ix_workspace_invitations_workspace_id");
            entity.HasIndex(invitation => invitation.InvitedByUserId)
                .HasDatabaseName(
                    "ix_workspace_invitations_invited_by_user_id");
            entity.HasIndex(invitation => invitation.ExpiresAt)
                .HasDatabaseName("ix_workspace_invitations_expires_at");
        });
    }
}
