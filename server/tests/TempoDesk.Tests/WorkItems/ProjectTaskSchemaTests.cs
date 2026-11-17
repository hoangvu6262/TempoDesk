using Microsoft.EntityFrameworkCore;
using Npgsql;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces.Persistence;
using TempoDesk.Tests.Database;

namespace TempoDesk.Tests.WorkItems;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class ProjectTaskSchemaTests(
    PostgreSqlFixture database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TaskProjectMustBelongToSameWorkspace()
    {
        var scenario = await CreateScenarioAsync();
        await using var context = database.CreateContext();
        var projectId = await InsertProjectAsync(
            context, scenario.FirstWorkspaceId, "First");

        await AssertConstraintAsync(
            "fk_tasks_project",
            PostgresErrorCodes.ForeignKeyViolation,
            () => InsertTaskAsync(
                context,
                scenario.SecondWorkspaceId,
                projectId,
                scenario.OwnerId));
    }

    [Fact]
    public async Task ParentMustShareWorkspaceAndProject()
    {
        var scenario = await CreateScenarioAsync();
        await using var context = database.CreateContext();
        var firstProjectId = await InsertProjectAsync(
            context, scenario.FirstWorkspaceId, "First");
        var secondProjectId = await InsertProjectAsync(
            context, scenario.FirstWorkspaceId, "Second");
        var parentId = await InsertTaskAsync(
            context,
            scenario.FirstWorkspaceId,
            firstProjectId,
            scenario.OwnerId);

        await AssertConstraintAsync(
            "fk_tasks_parent",
            PostgresErrorCodes.ForeignKeyViolation,
            () => InsertTaskAsync(
                context,
                scenario.FirstWorkspaceId,
                secondProjectId,
                scenario.OwnerId,
                parentId));
    }

    [Fact]
    public async Task TaskCannotParentItself()
    {
        var scenario = await CreateScenarioAsync();
        await using var context = database.CreateContext();
        var projectId = await InsertProjectAsync(
            context, scenario.FirstWorkspaceId, "Project");
        var taskId = Guid.NewGuid();

        await AssertConstraintAsync(
            "ck_tasks_not_self_parent",
            PostgresErrorCodes.CheckViolation,
            () => InsertTaskAsync(
                context,
                scenario.FirstWorkspaceId,
                projectId,
                scenario.OwnerId,
                taskId,
                taskId));
    }

    [Theory]
    [InlineData("blocked", "none", "manual", "ck_tasks_status")]
    [InlineData("todo", "critical", "manual", "ck_tasks_priority")]
    [InlineData("todo", "none", "github", "ck_tasks_source_type")]
    public async Task TaskValueSetsAreClosed(
        string status,
        string priority,
        string sourceType,
        string constraint)
    {
        var scenario = await CreateScenarioAsync();
        await using var context = database.CreateContext();
        var projectId = await InsertProjectAsync(
            context, scenario.FirstWorkspaceId, "Project");

        await AssertConstraintAsync(
            constraint,
            PostgresErrorCodes.CheckViolation,
            () => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.tasks
                    (id, workspace_id, project_id, created_by_user_id,
                     title, status, priority, source_type, sort_key,
                     created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {scenario.FirstWorkspaceId},
                     {projectId}, {scenario.OwnerId}, 'Invalid', {status},
                     {priority}, {sourceType}, 0, {Now}, {Now})
                """));
    }

    [Fact]
    public async Task InvalidDatesAndEstimateAreRejected()
    {
        var scenario = await CreateScenarioAsync();
        await using var context = database.CreateContext();
        var projectId = await InsertProjectAsync(
            context, scenario.FirstWorkspaceId, "Project");

        await AssertConstraintAsync(
            "ck_tasks_dates",
            PostgresErrorCodes.CheckViolation,
            () => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.tasks
                    (id, workspace_id, project_id, created_by_user_id,
                     title, start_date, due_date, sort_key,
                     created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {scenario.FirstWorkspaceId},
                     {projectId}, {scenario.OwnerId}, 'Dates',
                     {new DateOnly(2026, 11, 20)},
                     {new DateOnly(2026, 11, 19)}, 0, {Now}, {Now})
                """));
        await AssertConstraintAsync(
            "ck_tasks_estimated_minutes",
            PostgresErrorCodes.CheckViolation,
            () => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.tasks
                    (id, workspace_id, project_id, created_by_user_id,
                     title, estimated_minutes, sort_key,
                     created_at, updated_at)
                VALUES
                    ({Guid.NewGuid()}, {scenario.FirstWorkspaceId},
                     {projectId}, {scenario.OwnerId}, 'Estimate', 0, 0,
                     {Now}, {Now})
                """));
    }

    [Fact]
    public async Task DuplicateAssigneesAndTaskLabelsAreRejected()
    {
        var scenario = await CreateScenarioAsync();
        await using var context = database.CreateContext();
        var projectId = await InsertProjectAsync(
            context, scenario.FirstWorkspaceId, "Project");
        var taskId = await InsertTaskAsync(
            context,
            scenario.FirstWorkspaceId,
            projectId,
            scenario.OwnerId);
        var labelId = await InsertLabelAsync(
            context, scenario.FirstWorkspaceId, "Backend");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.task_assignees
                (workspace_id, task_id, user_id, assigned_at)
            VALUES
                ({scenario.FirstWorkspaceId}, {taskId},
                 {scenario.OwnerId}, {Now});
            INSERT INTO public.task_labels
                (workspace_id, task_id, label_id)
            VALUES
                ({scenario.FirstWorkspaceId}, {taskId}, {labelId})
            """);

        await AssertConstraintAsync(
            "pk_task_assignees",
            PostgresErrorCodes.UniqueViolation,
            () => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.task_assignees
                    (workspace_id, task_id, user_id, assigned_at)
                VALUES
                    ({scenario.FirstWorkspaceId}, {taskId},
                     {scenario.OwnerId}, {Now})
                """));
        await AssertConstraintAsync(
            "pk_task_labels",
            PostgresErrorCodes.UniqueViolation,
            () => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.task_labels
                    (workspace_id, task_id, label_id)
                VALUES
                    ({scenario.FirstWorkspaceId}, {taskId}, {labelId})
                """));
    }

    [Fact]
    public async Task LabelNamesAreUniqueIgnoringCase()
    {
        var scenario = await CreateScenarioAsync();
        await using var context = database.CreateContext();
        await InsertLabelAsync(context, scenario.FirstWorkspaceId, "Backend");

        await AssertConstraintAsync(
            "ux_labels_workspace_normalized_name",
            PostgresErrorCodes.UniqueViolation,
            () => InsertLabelAsync(
                context, scenario.FirstWorkspaceId, "backend"));
    }

    [Fact]
    public async Task CrossWorkspaceLabelCommentAndActivityAreRejected()
    {
        var scenario = await CreateScenarioAsync();
        await using var context = database.CreateContext();
        var projectId = await InsertProjectAsync(
            context, scenario.FirstWorkspaceId, "Project");
        var taskId = await InsertTaskAsync(
            context,
            scenario.FirstWorkspaceId,
            projectId,
            scenario.OwnerId);
        var labelId = await InsertLabelAsync(
            context, scenario.SecondWorkspaceId, "Other");

        await AssertConstraintAsync(
            "fk_task_labels_task",
            PostgresErrorCodes.ForeignKeyViolation,
            () => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.task_labels
                    (workspace_id, task_id, label_id)
                VALUES
                    ({scenario.SecondWorkspaceId}, {taskId}, {labelId})
                """));
        await AssertConstraintAsync(
            "fk_task_comments_task",
            PostgresErrorCodes.ForeignKeyViolation,
            () => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.task_comments
                    (id, workspace_id, task_id, author_user_id,
                     body, created_at)
                VALUES
                    ({Guid.NewGuid()}, {scenario.SecondWorkspaceId},
                     {taskId}, {scenario.OwnerId}, 'Comment', {Now})
                """));
        await AssertConstraintAsync(
            "fk_task_activity_logs_task",
            PostgresErrorCodes.ForeignKeyViolation,
            () => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.task_activity_logs
                    (id, workspace_id, task_id, actor_user_id,
                     event_type, created_at)
                VALUES
                    ({Guid.NewGuid()}, {scenario.SecondWorkspaceId},
                     {taskId}, {scenario.OwnerId}, 'task_created', {Now})
                """));
    }

    private static async Task AssertConstraintAsync(
        string constraint,
        string sqlState,
        Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(sqlState, error.SqlState);
        Assert.Equal(constraint, error.ConstraintName);
    }

    private async Task<Scenario> CreateScenarioAsync()
    {
        var owner = User($"owner-{Guid.NewGuid():N}@example.com");
        var first = Workspace("First", owner.Id);
        var second = Workspace("Second", owner.Id);
        await using var context = database.CreateContext();
        await using var transaction = await context.Database
            .BeginTransactionAsync();
        context.Users.Add(owner);
        context.Workspaces.AddRange(first, second);
        context.WorkspaceMembers.AddRange(
            Member(first.Id, owner.Id),
            Member(second.Id, owner.Id));
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(owner.Id, first.Id, second.Id);
    }

    private static async Task<Guid> InsertProjectAsync(
        TempoDeskIdentityDbContext context,
        Guid workspaceId,
        string name)
    {
        var id = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.projects
                (id, workspace_id, name, created_at, updated_at)
            VALUES ({id}, {workspaceId}, {name}, {Now}, {Now})
            """);
        return id;
    }

    private static async Task<Guid> InsertTaskAsync(
        TempoDeskIdentityDbContext context,
        Guid workspaceId,
        Guid projectId,
        Guid creatorId,
        Guid? parentId = null,
        Guid? taskId = null)
    {
        var id = taskId ?? Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.tasks
                (id, workspace_id, project_id, parent_task_id,
                 created_by_user_id, title, sort_key, created_at, updated_at)
            VALUES
                ({id}, {workspaceId}, {projectId}, {parentId},
                 {creatorId}, 'Task', 0, {Now}, {Now})
            """);
        return id;
    }

    private static async Task<Guid> InsertLabelAsync(
        TempoDeskIdentityDbContext context,
        Guid workspaceId,
        string name)
    {
        var id = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.labels
                (id, workspace_id, name, normalized_name, created_at)
            VALUES
                ({id}, {workspaceId}, {name}, {name.ToUpperInvariant()}, {Now})
            """);
        return id;
    }

    private static ApplicationUser User(string email) => new()
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

    private static Workspace Workspace(string name, Guid ownerId) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = WorkspaceTypeValues.Team,
        OwnerUserId = ownerId,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static WorkspaceMember Member(
        Guid workspaceId,
        Guid userId) => new()
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = "owner",
            CreatedAt = Now
        };

    private sealed record Scenario(
        Guid OwnerId,
        Guid FirstWorkspaceId,
        Guid SecondWorkspaceId);
}
