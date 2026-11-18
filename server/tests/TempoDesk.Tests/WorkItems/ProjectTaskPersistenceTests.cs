using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.WorkItems;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.WorkItems.Persistence;
using TempoDesk.Infrastructure.Workspaces.Persistence;
using TempoDesk.Tests.Database;

namespace TempoDesk.Tests.WorkItems;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class ProjectTaskPersistenceTests(
    PostgreSqlFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 16, 11, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CompleteGraphRoundTripsAndDeletedTaskIsFiltered()
    {
        var scenario = await CreateScenarioAsync();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.WorkspaceId,
            Name = "Delivery",
            CreatedAt = Now,
            UpdatedAt = Now
        };
        var task = new WorkTask
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.WorkspaceId,
            ProjectId = project.Id,
            CreatedByUserId = scenario.UserId,
            Title = "Ship schema",
            Priority = WorkItemPriorityValues.High,
            Status = WorkItemStatusValues.InProgress,
            SourceType = WorkItemSourceTypeValues.Manual,
            StartDate = new DateOnly(2026, 11, 16),
            DueDate = new DateOnly(2026, 11, 20),
            EstimatedMinutes = 90,
            SortKey = 1000m,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        var child = new WorkTask
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.WorkspaceId,
            ProjectId = project.Id,
            ParentTaskId = task.Id,
            CreatedByUserId = scenario.UserId,
            Title = "Verify schema",
            SortKey = 2000m,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        var label = new Label
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.WorkspaceId,
            Name = "Backend",
            NormalizedName = "BACKEND",
            CreatedAt = Now
        };
        task.Assignees.Add(new TaskAssignee
        {
            WorkspaceId = scenario.WorkspaceId,
            TaskId = task.Id,
            UserId = scenario.UserId,
            AssignedAt = Now
        });
        task.TaskLabels.Add(new TaskLabel
        {
            WorkspaceId = scenario.WorkspaceId,
            TaskId = task.Id,
            LabelId = label.Id,
            Label = label
        });
        task.Comments.Add(new TaskComment
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.WorkspaceId,
            TaskId = task.Id,
            AuthorUserId = scenario.UserId,
            Body = "Ready",
            CreatedAt = Now
        });
        task.ActivityLogs.Add(new TaskActivityLog
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.WorkspaceId,
            TaskId = task.Id,
            ActorUserId = scenario.UserId,
            EventType = "task_created",
            Details = JsonDocument.Parse("{\"status\":\"in_progress\"}"),
            CreatedAt = Now
        });
        project.Tasks.Add(task);
        project.Tasks.Add(child);

        await using (var context = database.CreateContext())
        {
            context.Projects.Add(project);
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var stored = await context.Tasks
                .Include(item => item.Children)
                .Include(item => item.Assignees)
                .Include(item => item.TaskLabels)
                .ThenInclude(link => link.Label)
                .Include(item => item.Comments)
                .Include(item => item.ActivityLogs)
                .SingleAsync(item => item.Id == task.Id);
            Assert.Single(stored.Children);
            Assert.Single(stored.Assignees);
            Assert.Equal(
                "Backend",
                Assert.Single(stored.TaskLabels).Label.Name);
            Assert.Equal("Ready", Assert.Single(stored.Comments).Body);
            Assert.Equal(
                "task_created",
                Assert.Single(stored.ActivityLogs).EventType);
            stored.DeletedAt = Now.AddMinutes(1);
            await context.SaveChangesAsync();
        }

        await using var verification = database.CreateContext();
        Assert.False(await verification.Tasks
            .AnyAsync(item => item.Id == task.Id));
        Assert.True(await verification.Tasks.IgnoreQueryFilters()
            .AnyAsync(item => item.Id == task.Id));
    }

    [Fact]
    public async Task UpdatingTaskAdvancesRowVersion()
    {
        var seeded = await SeedTaskAsync();
        await using var context = database.CreateContext();
        var task = await context.Tasks.SingleAsync(
            item => item.Id == seeded.TaskId);
        var version = task.RowVersion;

        task.Title = "Updated";
        await context.SaveChangesAsync();

        Assert.Equal(version + 1, task.RowVersion);
    }

    [Fact]
    public async Task StaleTaskUpdateThrowsConcurrencyException()
    {
        var seeded = await SeedTaskAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstTask = await first.Tasks.SingleAsync(
            item => item.Id == seeded.TaskId);
        var secondTask = await second.Tasks.SingleAsync(
            item => item.Id == seeded.TaskId);

        firstTask.Title = "First";
        await first.SaveChangesAsync();
        secondTask.Title = "Stale";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => second.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingProjectCascadesTaskOwnedRecords()
    {
        var seeded = await SeedTaskAsync();
        await using (var context = database.CreateContext())
        {
            var labelId = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.labels
                    (id, workspace_id, name, normalized_name, created_at)
                VALUES
                    ({labelId}, {seeded.WorkspaceId}, 'Backend',
                     'BACKEND', {Now});
                INSERT INTO public.task_assignees
                    (workspace_id, task_id, user_id, assigned_at)
                VALUES
                    ({seeded.WorkspaceId}, {seeded.TaskId},
                     {seeded.UserId}, {Now});
                INSERT INTO public.task_labels
                    (workspace_id, task_id, label_id)
                VALUES
                    ({seeded.WorkspaceId}, {seeded.TaskId}, {labelId});
                INSERT INTO public.task_comments
                    (id, workspace_id, task_id, author_user_id,
                     body, created_at)
                VALUES
                    ({Guid.NewGuid()}, {seeded.WorkspaceId}, {seeded.TaskId},
                     {seeded.UserId}, 'Ready', {Now});
                INSERT INTO public.task_activity_logs
                    (id, workspace_id, task_id, actor_user_id,
                     event_type, created_at)
                VALUES
                    ({Guid.NewGuid()}, {seeded.WorkspaceId}, {seeded.TaskId},
                     {seeded.UserId}, 'task_created', {Now})
                """);
            context.Projects.Remove(await context.Projects.SingleAsync(
                project => project.Id == seeded.ProjectId));
            await context.SaveChangesAsync();
        }

        await using var verification = database.CreateContext();
        Assert.Empty(await verification.Tasks.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verification.TaskAssignees.ToListAsync());
        Assert.Empty(await verification.TaskLabels.ToListAsync());
        Assert.Empty(await verification.TaskComments.ToListAsync());
        Assert.Empty(await verification.TaskActivityLogs.ToListAsync());
    }

    [Fact]
    public async Task ActiveTaskIndexesExcludeDeletedRows()
    {
        await using var context = database.CreateContext();
        var definitions = await context.Database.SqlQueryRaw<string>("""
            SELECT indexdef AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname IN (
                  'ix_tasks_workspace_project_active',
                  'ix_tasks_workspace_due_active')
            ORDER BY indexname
            """).ToListAsync();

        Assert.Equal(2, definitions.Count);
        Assert.All(definitions, definition =>
            Assert.Contains("deleted_at IS NULL", definition));
    }

    private async Task<SeededTask> SeedTaskAsync()
    {
        var scenario = await CreateScenarioAsync();
        var project = new Project
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.WorkspaceId,
            Name = "Delivery",
            CreatedAt = Now,
            UpdatedAt = Now
        };
        var task = new WorkTask
        {
            Id = Guid.NewGuid(),
            WorkspaceId = scenario.WorkspaceId,
            ProjectId = project.Id,
            CreatedByUserId = scenario.UserId,
            Title = "Task",
            SortKey = 1000m,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        project.Tasks.Add(task);
        await using var context = database.CreateContext();
        context.Projects.Add(project);
        await context.SaveChangesAsync();
        return new(
            scenario.UserId,
            scenario.WorkspaceId,
            project.Id,
            task.Id);
    }

    private async Task<Scenario> CreateScenarioAsync()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = Now,
            UpdatedAt = Now
        };
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Delivery",
            Type = WorkspaceTypeValues.Team,
            OwnerUserId = user.Id,
            CreatedAt = Now,
            UpdatedAt = Now
        };
        await using var context = database.CreateContext();
        await using var transaction = await context.Database
            .BeginTransactionAsync();
        context.Users.Add(user);
        context.Workspaces.Add(workspace);
        context.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = user.Id,
            Role = "owner",
            CreatedAt = Now
        });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(user.Id, workspace.Id);
    }

    private sealed record Scenario(Guid UserId, Guid WorkspaceId);

    private sealed record SeededTask(
        Guid UserId,
        Guid WorkspaceId,
        Guid ProjectId,
        Guid TaskId);
}
