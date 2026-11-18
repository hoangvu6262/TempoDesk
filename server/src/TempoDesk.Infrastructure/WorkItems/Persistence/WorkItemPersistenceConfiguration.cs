using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.WorkItems;

namespace TempoDesk.Infrastructure.WorkItems.Persistence;

public static class WorkItemPersistenceConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        ConfigureProjects(builder);
        ConfigureTasks(builder);
        ConfigureAssignees(builder);
        ConfigureLabels(builder);
        ConfigureTaskLabels(builder);
        ConfigureComments(builder);
        ConfigureActivity(builder);
    }

    private static void ConfigureProjects(ModelBuilder builder)
    {
        builder.Entity<Project>(entity =>
        {
            entity.ToTable("projects", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_projects_name_not_empty", "btrim(name) <> ''");
                table.HasCheckConstraint(
                    "ck_projects_updated_at", "updated_at >= created_at");
                table.HasCheckConstraint(
                    "ck_projects_archived_at",
                    "archived_at IS NULL OR archived_at >= created_at");
            });
            entity.HasKey(project => project.Id);
            entity.HasAlternateKey(project => new
                { project.WorkspaceId, project.Id })
                .HasName("uq_projects_workspace_id");
            entity.Property(project => project.Id).HasColumnName("id");
            entity.Property(project => project.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(project => project.Name)
                .HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(project => project.Description)
                .HasColumnName("description");
            entity.Property(project => project.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(project => project.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(project => project.ArchivedAt)
                .HasColumnName("archived_at");
            entity.HasOne(project => project.Workspace)
                .WithMany()
                .HasForeignKey(project => project.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_projects_workspace");
            entity.HasIndex(project => new
                { project.WorkspaceId, project.Name, project.Id })
                .HasDatabaseName("ix_projects_workspace_active")
                .HasFilter("archived_at IS NULL");
        });
    }

    private static void ConfigureTasks(ModelBuilder builder)
    {
        builder.Entity<WorkTask>(entity =>
        {
            entity.ToTable("tasks", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_tasks_not_self_parent",
                    "parent_task_id IS NULL OR parent_task_id <> id");
                table.HasCheckConstraint(
                    "ck_tasks_title_not_empty", "btrim(title) <> ''");
                table.HasCheckConstraint(
                    "ck_tasks_priority",
                    "priority IN ('none', 'low', 'medium', 'high', 'urgent')");
                table.HasCheckConstraint(
                    "ck_tasks_status",
                    "status IN ('todo', 'in_progress', 'done')");
                table.HasCheckConstraint(
                    "ck_tasks_source_type",
                    "source_type IN ('manual', 'jira')");
                table.HasCheckConstraint(
                    "ck_tasks_dates",
                    "start_date IS NULL OR due_date IS NULL OR due_date >= start_date");
                table.HasCheckConstraint(
                    "ck_tasks_estimated_minutes",
                    "estimated_minutes IS NULL OR estimated_minutes > 0");
                table.HasCheckConstraint(
                    "ck_tasks_row_version", "row_version > 0");
                table.HasCheckConstraint(
                    "ck_tasks_updated_at", "updated_at >= created_at");
                table.HasCheckConstraint(
                    "ck_tasks_deleted_at",
                    "deleted_at IS NULL OR deleted_at >= created_at");
            });
            entity.HasKey(task => task.Id);
            entity.HasAlternateKey(task => new
                { task.WorkspaceId, task.Id })
                .HasName("uq_tasks_workspace_id");
            entity.HasAlternateKey(task => new
                { task.WorkspaceId, task.ProjectId, task.Id })
                .HasName("uq_tasks_workspace_project_id");
            entity.Property(task => task.Id).HasColumnName("id");
            entity.Property(task => task.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(task => task.ProjectId)
                .HasColumnName("project_id");
            entity.Property(task => task.ParentTaskId)
                .HasColumnName("parent_task_id");
            entity.Property(task => task.CreatedByUserId)
                .HasColumnName("created_by_user_id");
            entity.Property(task => task.Title)
                .HasColumnName("title").HasMaxLength(300).IsRequired();
            entity.Property(task => task.Description)
                .HasColumnName("description");
            entity.Property(task => task.Priority)
                .HasColumnName("priority").HasMaxLength(20)
                .HasDefaultValue(WorkItemPriorityValues.None).IsRequired();
            entity.Property(task => task.Status)
                .HasColumnName("status").HasMaxLength(20)
                .HasDefaultValue(WorkItemStatusValues.Todo).IsRequired();
            entity.Property(task => task.StartDate)
                .HasColumnName("start_date");
            entity.Property(task => task.DueDate).HasColumnName("due_date");
            entity.Property(task => task.EstimatedMinutes)
                .HasColumnName("estimated_minutes");
            entity.Property(task => task.SourceType)
                .HasColumnName("source_type").HasMaxLength(20)
                .HasDefaultValue(WorkItemSourceTypeValues.Manual).IsRequired();
            entity.Property(task => task.SortKey)
                .HasColumnName("sort_key").HasPrecision(30, 15)
                .HasDefaultValue(0m);
            entity.Property(task => task.RowVersion)
                .HasColumnName("row_version").HasDefaultValue(1L)
                .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            entity.Property(task => task.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(task => task.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .ValueGeneratedOnAddOrUpdate();
            entity.Property(task => task.DeletedAt)
                .HasColumnName("deleted_at");
            entity.HasOne(task => task.Project)
                .WithMany(project => project.Tasks)
                .HasForeignKey(task => new
                    { task.WorkspaceId, task.ProjectId })
                .HasPrincipalKey(project => new
                    { project.WorkspaceId, project.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tasks_project");
            entity.HasOne(task => task.Parent)
                .WithMany(parent => parent.Children)
                .HasForeignKey(task => new
                    { task.WorkspaceId, task.ProjectId, task.ParentTaskId })
                .HasPrincipalKey(parent => new
                    { parent.WorkspaceId, parent.ProjectId, parent.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tasks_parent");
            entity.HasOne(task => task.CreatedByUser)
                .WithMany()
                .HasForeignKey(task => task.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_tasks_creator");
            entity.HasIndex(task => new
                { task.WorkspaceId, task.ProjectId, task.SortKey, task.Id })
                .HasDatabaseName("ix_tasks_workspace_project_active")
                .HasFilter("deleted_at IS NULL");
            entity.HasIndex(task => new
                { task.ParentTaskId, task.SortKey, task.Id })
                .HasDatabaseName("ix_tasks_parent_active")
                .HasFilter(
                    "parent_task_id IS NOT NULL AND deleted_at IS NULL");
            entity.HasIndex(task => new
                { task.WorkspaceId, task.DueDate, task.Id })
                .HasDatabaseName("ix_tasks_workspace_due_active")
                .HasFilter("due_date IS NOT NULL AND deleted_at IS NULL");
            entity.HasQueryFilter(task => task.DeletedAt == null);
        });
    }

    private static void ConfigureAssignees(ModelBuilder builder)
    {
        builder.Entity<TaskAssignee>(entity =>
        {
            entity.ToTable("task_assignees", "public");
            entity.HasKey(assignee => new
                { assignee.TaskId, assignee.UserId })
                .HasName("pk_task_assignees");
            entity.Property(assignee => assignee.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(assignee => assignee.TaskId)
                .HasColumnName("task_id");
            entity.Property(assignee => assignee.UserId)
                .HasColumnName("user_id");
            entity.Property(assignee => assignee.AssignedAt)
                .HasColumnName("assigned_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(assignee => assignee.Task)
                .WithMany(task => task.Assignees)
                .HasForeignKey(assignee => new
                    { assignee.WorkspaceId, assignee.TaskId })
                .HasPrincipalKey(task => new
                    { task.WorkspaceId, task.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_task_assignees_task");
            entity.HasOne(assignee => assignee.User)
                .WithMany()
                .HasForeignKey(assignee => assignee.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_task_assignees_user");
            entity.HasIndex(assignee => new
                { assignee.UserId, assignee.WorkspaceId, assignee.TaskId })
                .HasDatabaseName("ix_task_assignees_user_workspace");
        });
    }

    private static void ConfigureLabels(ModelBuilder builder)
    {
        builder.Entity<Label>(entity =>
        {
            entity.ToTable("labels", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_labels_name_not_empty", "btrim(name) <> ''");
                table.HasCheckConstraint(
                    "ck_labels_normalized_name_not_empty",
                    "btrim(normalized_name) <> ''");
            });
            entity.HasKey(label => label.Id);
            entity.HasAlternateKey(label => new
                { label.WorkspaceId, label.Id })
                .HasName("uq_labels_workspace_id");
            entity.Property(label => label.Id).HasColumnName("id");
            entity.Property(label => label.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(label => label.Name)
                .HasColumnName("name").HasMaxLength(100).IsRequired();
            entity.Property(label => label.NormalizedName)
                .HasColumnName("normalized_name").HasMaxLength(100)
                .IsRequired();
            entity.Property(label => label.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(label => label.Workspace)
                .WithMany()
                .HasForeignKey(label => label.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_labels_workspace");
            entity.HasIndex(label => new
                { label.WorkspaceId, label.NormalizedName })
                .HasDatabaseName("ux_labels_workspace_normalized_name")
                .IsUnique();
        });
    }

    private static void ConfigureTaskLabels(ModelBuilder builder)
    {
        builder.Entity<TaskLabel>(entity =>
        {
            entity.ToTable("task_labels", "public");
            entity.HasKey(link => new { link.TaskId, link.LabelId })
                .HasName("pk_task_labels");
            entity.Property(link => link.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(link => link.TaskId).HasColumnName("task_id");
            entity.Property(link => link.LabelId).HasColumnName("label_id");
            entity.HasOne(link => link.Task)
                .WithMany(task => task.TaskLabels)
                .HasForeignKey(link => new { link.WorkspaceId, link.TaskId })
                .HasPrincipalKey(task => new
                    { task.WorkspaceId, task.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_task_labels_task");
            entity.HasOne(link => link.Label)
                .WithMany(label => label.TaskLabels)
                .HasForeignKey(link => new { link.WorkspaceId, link.LabelId })
                .HasPrincipalKey(label => new
                    { label.WorkspaceId, label.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_task_labels_label");
            entity.HasIndex(link => new { link.LabelId, link.TaskId })
                .HasDatabaseName("ix_task_labels_label_id");
        });
    }

    private static void ConfigureComments(ModelBuilder builder)
    {
        builder.Entity<TaskComment>(entity =>
        {
            entity.ToTable("task_comments", "public", table =>
                table.HasCheckConstraint(
                    "ck_task_comments_body_not_empty", "btrim(body) <> ''"));
            entity.HasKey(comment => comment.Id);
            entity.Property(comment => comment.Id).HasColumnName("id");
            entity.Property(comment => comment.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(comment => comment.TaskId)
                .HasColumnName("task_id");
            entity.Property(comment => comment.AuthorUserId)
                .HasColumnName("author_user_id");
            entity.Property(comment => comment.Body)
                .HasColumnName("body").IsRequired();
            entity.Property(comment => comment.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(comment => comment.Task)
                .WithMany(task => task.Comments)
                .HasForeignKey(comment => new
                    { comment.WorkspaceId, comment.TaskId })
                .HasPrincipalKey(task => new
                    { task.WorkspaceId, task.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_task_comments_task");
            entity.HasOne(comment => comment.AuthorUser)
                .WithMany()
                .HasForeignKey(comment => comment.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_task_comments_author");
            entity.HasIndex(comment => new
                { comment.TaskId, comment.CreatedAt, comment.Id })
                .HasDatabaseName("ix_task_comments_task_created");
        });
    }

    private static void ConfigureActivity(ModelBuilder builder)
    {
        builder.Entity<TaskActivityLog>(entity =>
        {
            entity.ToTable("task_activity_logs", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_task_activity_logs_event_type_not_empty",
                    "btrim(event_type) <> ''");
                table.HasCheckConstraint(
                    "ck_task_activity_logs_details_object",
                    "jsonb_typeof(details) = 'object'");
            });
            entity.HasKey(activity => activity.Id);
            entity.Property(activity => activity.Id).HasColumnName("id");
            entity.Property(activity => activity.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(activity => activity.TaskId)
                .HasColumnName("task_id");
            entity.Property(activity => activity.ActorUserId)
                .HasColumnName("actor_user_id");
            entity.Property(activity => activity.EventType)
                .HasColumnName("event_type").HasMaxLength(50).IsRequired();
            entity.Property(activity => activity.Details)
                .HasColumnName("details").HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb");
            entity.Property(activity => activity.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(activity => activity.Task)
                .WithMany(task => task.ActivityLogs)
                .HasForeignKey(activity => new
                    { activity.WorkspaceId, activity.TaskId })
                .HasPrincipalKey(task => new
                    { task.WorkspaceId, task.Id })
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_task_activity_logs_task");
            entity.HasOne(activity => activity.ActorUser)
                .WithMany()
                .HasForeignKey(activity => activity.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_task_activity_logs_actor");
            entity.HasIndex(activity => new
                { activity.TaskId, activity.CreatedAt, activity.Id })
                .HasDatabaseName("ix_task_activity_logs_task_created");
        });
    }
}
