# SCRUM-38 Project and Task Data Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the PostgreSQL schema, EF Core persistence mappings, and integration coverage for projects, tasks, subtasks, assignments, labels, comments, activity, soft deletion, and optimistic concurrency.

**Architecture:** Keep the repository SQL-first: one immutable migration owns relational invariants and indexes, while a focused `WorkItems.Persistence` module maps the same schema into the existing identity context. PostgreSQL enforces tenant-safe relationships and increments task versions; EF provides default soft-delete filtering and stale-write detection.

**Tech Stack:** PostgreSQL 18.6, .NET 10, EF Core 10, Npgsql 10.0.3, xUnit 2.9.3, Testcontainers.PostgreSql 4.16.0.

## Global Constraints

- Do not add `project_members`; projects inherit workspace access.
- Store task scheduling fields as PostgreSQL `date` and C# `DateOnly?`.
- Statuses: `todo`, `in_progress`, `done`.
- Priorities: `none`, `low`, `medium`, `high`, `urgent`.
- Sources: `manual`, `jira`.
- Project archival uses `archived_at`; task soft deletion uses `deleted_at`.
- Label uniqueness is `(workspace_id, normalized_name)` without `citext`.
- Active assignee membership remains an SCRUM-39 application rule.
- Do not add APIs, DTOs, services, boards, attachments, dependencies, or UI.
- Add a new migration; never edit an applied migration.

## File Map

- Create `database/migrations/202611161730_create_project_task_schema.sql`: tables, constraints, indexes, and task update trigger.
- Create `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/*.cs`: seven entities, constants, and one mapping entry point.
- Modify `server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs`: new `DbSet` properties and configuration call.
- Modify `server/tests/TempoDesk.Tests/Database/PostgreSqlFixture.cs`: reset new tables.
- Create `server/tests/TempoDesk.Tests/WorkItems/ProjectTaskSchemaTests.cs`: database invariant coverage.
- Create `server/tests/TempoDesk.Tests/WorkItems/ProjectTaskPersistenceTests.cs`: EF graph, filter, and concurrency coverage.
- Modify `database/README.md`: align the documented mapping workflow with explicit mappings.

---

### Task 1: PostgreSQL schema and relational invariants

**Files:**
- Create: `database/migrations/202611161730_create_project_task_schema.sql`
- Create: `server/tests/TempoDesk.Tests/WorkItems/ProjectTaskSchemaTests.cs`
- Modify: `server/tests/TempoDesk.Tests/Database/PostgreSqlFixture.cs:113-126`

**Interfaces:**
- Consumes: `public.users`, `public.workspaces`, and the migration test fixture.
- Produces: seven SCRUM-38 tables plus `public.set_task_update_metadata()`.

- [ ] **Step 1: Write failing database tests**

Create `ProjectTaskSchemaTests.cs` using the existing `PostgreSqlFixture`. Seed users/workspaces/owner memberships with the same helpers used by `WorkspaceInvitationTests.WorkspaceScenario`. Add these exact tests:

```csharp
[Fact] public async Task TaskProjectMustBelongToSameWorkspace();
[Fact] public async Task ParentMustShareWorkspaceAndProject();
[Fact] public async Task TaskCannotParentItself();
[Theory]
[InlineData("blocked", "none", "manual")]
[InlineData("todo", "critical", "manual")]
[InlineData("todo", "none", "github")]
public async Task TaskValueSetsAreClosed(
    string status, string priority, string sourceType);
[Fact] public async Task InvalidDatesAndEstimateAreRejected();
[Fact] public async Task DuplicateAssigneesAndTaskLabelsAreRejected();
[Fact] public async Task LabelNamesAreUniqueIgnoringCase();
[Fact] public async Task CrossWorkspaceLabelCommentAndActivityAreRejected();
```

Each test must call `context.Database.ExecuteSqlInterpolatedAsync`, capture `PostgresException`, and assert both `SqlState` and the named `ConstraintName`. For example:

```csharp
var error = await Assert.ThrowsAsync<PostgresException>(() =>
    context.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO public.tasks
            (id, workspace_id, project_id, created_by_user_id,
             title, sort_key, created_at, updated_at)
        VALUES
            ({Guid.NewGuid()}, {otherWorkspaceId}, {projectId}, {ownerId},
             'Cross tenant', 0, {Now}, {Now})
        """));
Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
Assert.Equal("fk_tasks_project", error.ConstraintName);
```

For duplicates, assert `PostgresErrorCodes.UniqueViolation`; for invalid enums/dates/estimate, assert `PostgresErrorCodes.CheckViolation`. Use a new GUID/email per test so the collection-scoped database remains isolated.

- [ ] **Step 2: Run the focused tests and verify red**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter FullyQualifiedName~ProjectTaskSchemaTests
```

Expected: FAIL with PostgreSQL `42P01`, because `public.projects` does not exist.

- [ ] **Step 3: Create the migration**

Implement this exact table contract:

```text
projects
  id uuid PK
  workspace_id uuid NOT NULL FK workspaces(id) ON DELETE CASCADE
  name varchar(200) NOT NULL
  description text NULL
  created_at/updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
  archived_at timestamptz NULL
  UNIQUE(workspace_id, id)

tasks
  id uuid PK
  workspace_id/project_id uuid NOT NULL
  parent_task_id uuid NULL
  created_by_user_id uuid NOT NULL FK users(id) ON DELETE RESTRICT
  title varchar(300) NOT NULL; description text NULL
  priority varchar(20) DEFAULT 'none'; status varchar(20) DEFAULT 'todo'
  start_date/due_date date NULL; estimated_minutes integer NULL
  source_type varchar(20) DEFAULT 'manual'
  sort_key numeric(30,15) DEFAULT 0
  row_version bigint DEFAULT 1
  created_at/updated_at timestamptz DEFAULT CURRENT_TIMESTAMP
  deleted_at timestamptz NULL
  UNIQUE(workspace_id, id)
  UNIQUE(workspace_id, project_id, id)

task_assignees
  workspace_id/task_id/user_id uuid NOT NULL
  assigned_at timestamptz DEFAULT CURRENT_TIMESTAMP
  PRIMARY KEY(task_id, user_id)

labels
  id uuid PK; workspace_id uuid NOT NULL
  name/normalized_name varchar(100) NOT NULL
  created_at timestamptz DEFAULT CURRENT_TIMESTAMP
  UNIQUE(workspace_id, id)

task_labels
  workspace_id/task_id/label_id uuid NOT NULL
  PRIMARY KEY(task_id, label_id)

task_comments
  id uuid PK; workspace_id/task_id/author_user_id uuid NOT NULL
  body text NOT NULL; created_at timestamptz DEFAULT CURRENT_TIMESTAMP

task_activity_logs
  id uuid PK; workspace_id/task_id/actor_user_id uuid NOT NULL
  event_type varchar(50) NOT NULL
  details jsonb NOT NULL DEFAULT '{}'::jsonb
  created_at timestamptz DEFAULT CURRENT_TIMESTAMP
```

Use these exact relationship names and column orders:

```sql
CONSTRAINT fk_tasks_project
  FOREIGN KEY (workspace_id, project_id)
  REFERENCES public.projects (workspace_id, id) ON DELETE CASCADE;
CONSTRAINT fk_tasks_parent
  FOREIGN KEY (workspace_id, project_id, parent_task_id)
  REFERENCES public.tasks (workspace_id, project_id, id) ON DELETE CASCADE;
CONSTRAINT fk_task_assignees_task
  FOREIGN KEY (workspace_id, task_id)
  REFERENCES public.tasks (workspace_id, id) ON DELETE CASCADE;
CONSTRAINT fk_task_labels_task
  FOREIGN KEY (workspace_id, task_id)
  REFERENCES public.tasks (workspace_id, id) ON DELETE CASCADE;
CONSTRAINT fk_task_labels_label
  FOREIGN KEY (workspace_id, label_id)
  REFERENCES public.labels (workspace_id, id) ON DELETE CASCADE;
```

Comments and activity use the same `(workspace_id, task_id)` task foreign key. User references are `ON DELETE RESTRICT`. Name checks use `btrim(value) <> ''`. Add named checks for allowed value sets, `due_date >= start_date`, positive estimate/version, self-parenting, and timestamp ordering. Require activity `details` to be a JSON object.

Add exactly these query indexes:

```sql
CREATE INDEX ix_projects_workspace_active
  ON public.projects (workspace_id, name, id)
  WHERE archived_at IS NULL;
CREATE INDEX ix_tasks_workspace_project_active
  ON public.tasks (workspace_id, project_id, sort_key, id)
  WHERE deleted_at IS NULL;
CREATE INDEX ix_tasks_parent_active
  ON public.tasks (parent_task_id, sort_key, id)
  WHERE parent_task_id IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX ix_tasks_workspace_due_active
  ON public.tasks (workspace_id, due_date, id)
  WHERE due_date IS NOT NULL AND deleted_at IS NULL;
CREATE INDEX ix_task_assignees_user_workspace
  ON public.task_assignees (user_id, workspace_id, task_id);
CREATE UNIQUE INDEX ux_labels_workspace_normalized_name
  ON public.labels (workspace_id, normalized_name);
CREATE INDEX ix_task_labels_label_id
  ON public.task_labels (label_id, task_id);
CREATE INDEX ix_task_comments_task_created
  ON public.task_comments (task_id, created_at, id);
CREATE INDEX ix_task_activity_logs_task_created
  ON public.task_activity_logs (task_id, created_at, id);
```

Add the trigger:

```sql
CREATE FUNCTION public.set_task_update_metadata()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    NEW.row_version := OLD.row_version + 1;
    NEW.updated_at := CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;

CREATE TRIGGER set_task_update_metadata
BEFORE UPDATE ON public.tasks
FOR EACH ROW EXECUTE FUNCTION public.set_task_update_metadata();
```

Begin the file with rollback guidance: before production data, drop dependent tables in reverse order and then the function; after release, use a forward migration.

- [ ] **Step 4: Update fixture reset order**

Put these tables before `workspace_invitations` in the existing `TRUNCATE` statement:

```sql
public.task_activity_logs,
public.task_comments,
public.task_labels,
public.labels,
public.task_assignees,
public.tasks,
public.projects,
```

- [ ] **Step 5: Run focused schema tests**

Run Step 2 again. Expected: all schema tests PASS.

- [ ] **Step 6: Commit**

```bash
git add database/migrations/202611161730_create_project_task_schema.sql \
  server/tests/TempoDesk.Tests/Database/PostgreSqlFixture.cs \
  server/tests/TempoDesk.Tests/WorkItems/ProjectTaskSchemaTests.cs
git commit -m "feat(database): add project and task schema"
```

---

### Task 2: EF entities and schema-matching mappings

**Files:**
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/Project.cs`
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/WorkTask.cs`
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/TaskAssignee.cs`
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/Label.cs`
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/TaskLabel.cs`
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/TaskComment.cs`
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/TaskActivityLog.cs`
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/WorkItemValues.cs`
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/WorkItemPersistenceConfiguration.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs:1-43`
- Create: `server/tests/TempoDesk.Tests/WorkItems/ProjectTaskPersistenceTests.cs`

**Interfaces:**
- Consumes: Task 1's exact SQL names.
- Produces: `Projects`, `Tasks`, `TaskAssignees`, `Labels`, `TaskLabels`, `TaskComments`, and `TaskActivityLogs` sets.

- [ ] **Step 1: Write a failing EF graph/filter test**

Create a project with a parent task, child, assignee, label link, comment, and activity through navigations. Save it, reload with `Include`/`ThenInclude`, and assert all seven record types round-trip. Then set `DeletedAt`, save, and assert:

```csharp
Assert.False(await context.Tasks.AnyAsync(item => item.Id == taskId));
Assert.True(await context.Tasks.IgnoreQueryFilters()
    .AnyAsync(item => item.Id == taskId));
```

Use `JsonDocument.Parse("{\"status\":\"in_progress\"}")` for activity details. Run:

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter FullyQualifiedName~ProjectTaskPersistenceTests
```

Expected: FAIL to compile because the new persistence types do not exist.

- [ ] **Step 2: Add stable constants and entities**

Define public constants in `WorkItemValues.cs` for every allowed status, priority, and source value from Global Constraints.

Use these CLR shapes exactly:

```csharp
Project: Guid Id/WorkspaceId; string Name; string? Description;
  DateTimeOffset CreatedAt/UpdatedAt; DateTimeOffset? ArchivedAt;
  Workspace Workspace; ICollection<WorkTask> Tasks.

WorkTask: Guid Id/WorkspaceId/ProjectId/CreatedByUserId;
  Guid? ParentTaskId; string Title; string? Description;
  string Priority/Status/SourceType; DateOnly? StartDate/DueDate;
  int? EstimatedMinutes; decimal SortKey; long RowVersion;
  DateTimeOffset CreatedAt/UpdatedAt; DateTimeOffset? DeletedAt;
  Project Project; WorkTask? Parent; ApplicationUser CreatedByUser;
  ICollection<WorkTask> Children; ICollection<TaskAssignee> Assignees;
  ICollection<TaskLabel> TaskLabels; ICollection<TaskComment> Comments;
  ICollection<TaskActivityLog> ActivityLogs.

TaskAssignee: Guid WorkspaceId/TaskId/UserId; DateTimeOffset AssignedAt;
  WorkTask Task; ApplicationUser User.
Label: Guid Id/WorkspaceId; string Name/NormalizedName;
  DateTimeOffset CreatedAt; Workspace Workspace;
  ICollection<TaskLabel> TaskLabels.
TaskLabel: Guid WorkspaceId/TaskId/LabelId; WorkTask Task; Label Label.
TaskComment: Guid Id/WorkspaceId/TaskId/AuthorUserId; string Body;
  DateTimeOffset CreatedAt; WorkTask Task; ApplicationUser AuthorUser.
TaskActivityLog: Guid Id/WorkspaceId/TaskId/ActorUserId;
  string EventType; JsonDocument Details; DateTimeOffset CreatedAt;
  WorkTask Task; ApplicationUser ActorUser.
```

Initialize strings to `""`, collections to `[]`, references to `null!`, task status/priority/source to their constants, and activity details to `JsonDocument.Parse("{}")`.

- [ ] **Step 3: Map every SQL object explicitly**

Create one public `Configure(ModelBuilder)` entry point and private methods per table. Map every table/column/constraint/index by exact SQL name. Match composite key order exactly.

The critical mappings are:

```csharp
entity.HasOne(task => task.Project)
    .WithMany(project => project.Tasks)
    .HasForeignKey(task => new { task.WorkspaceId, task.ProjectId })
    .HasPrincipalKey(project => new { project.WorkspaceId, project.Id })
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

entity.Property(task => task.SortKey)
    .HasColumnName("sort_key").HasPrecision(30, 15)
    .HasDefaultValue(0m);
entity.Property(task => task.RowVersion)
    .HasColumnName("row_version").HasDefaultValue(1L)
    .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
entity.Property(task => task.UpdatedAt)
    .HasColumnName("updated_at")
    .HasDefaultValueSql("CURRENT_TIMESTAMP")
    .ValueGeneratedOnAddOrUpdate();
entity.HasQueryFilter(task => task.DeletedAt == null);

entity.Property(activity => activity.Details)
    .HasColumnName("details").HasColumnType("jsonb")
    .HasDefaultValueSql("'{}'::jsonb");
```

Assignee, task-label, comment, and activity task relationships use `(WorkspaceId, TaskId)` and task principal key `(WorkspaceId, Id)`. Task-label's label relationship uses `(WorkspaceId, LabelId)` and label principal key `(WorkspaceId, Id)`.

- [ ] **Step 4: Register configuration and DbSets**

Add `using TempoDesk.Infrastructure.WorkItems.Persistence;`, call `WorkItemPersistenceConfiguration.Configure(builder);`, and expose:

```csharp
public DbSet<Project> Projects => Set<Project>();
public DbSet<WorkTask> Tasks => Set<WorkTask>();
public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
public DbSet<Label> Labels => Set<Label>();
public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();
public DbSet<TaskComment> TaskComments => Set<TaskComment>();
public DbSet<TaskActivityLog> TaskActivityLogs => Set<TaskActivityLog>();
```

- [ ] **Step 5: Verify and commit**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter FullyQualifiedName~ProjectTaskPersistenceTests
dotnet build server/TempoDesk.sln
git add server/src/TempoDesk.Infrastructure/WorkItems \
  server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs \
  server/tests/TempoDesk.Tests/WorkItems/ProjectTaskPersistenceTests.cs
git commit -m "feat(infrastructure): map project and task persistence"
```

Expected: focused test passes; build has zero warnings and errors.

---

### Task 3: Concurrency, lifecycle, documentation, and final verification

**Files:**
- Modify: `server/tests/TempoDesk.Tests/WorkItems/ProjectTaskPersistenceTests.cs`
- Modify: `server/tests/TempoDesk.Tests/WorkItems/ProjectTaskSchemaTests.cs`
- Modify: `database/README.md`

**Interfaces:**
- Consumes: task update trigger and EF concurrency mapping.
- Produces: acceptance proof and an accurate database workflow.

- [ ] **Step 1: Add failing concurrency tests**

Add:

```csharp
[Fact]
public async Task UpdatingTaskAdvancesRowVersion()
{
    var taskId = await SeedTaskAsync();
    await using var context = database.CreateContext();
    var task = await context.Tasks.SingleAsync(item => item.Id == taskId);
    var version = task.RowVersion;
    task.Title = "Updated";
    await context.SaveChangesAsync();
    Assert.Equal(version + 1, task.RowVersion);
}

[Fact]
public async Task StaleTaskUpdateThrowsConcurrencyException()
{
    var taskId = await SeedTaskAsync();
    await using var first = database.CreateContext();
    await using var second = database.CreateContext();
    var firstTask = await first.Tasks.SingleAsync(item => item.Id == taskId);
    var secondTask = await second.Tasks.SingleAsync(item => item.Id == taskId);
    firstTask.Title = "First";
    await first.SaveChangesAsync();
    secondTask.Title = "Stale";
    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
        () => second.SaveChangesAsync());
}
```

Run only these tests. Expected before the final mapping adjustment: at least the in-memory version assertion fails if Npgsql does not return trigger-generated values.

- [ ] **Step 2: Preserve the database trigger as the authority**

Inspect emitted SQL. It must include the original `row_version` in the update predicate and return the new version. If the generated values are not refreshed, add:

```csharp
entity.Property(task => task.RowVersion).Metadata
    .SetAfterSaveBehavior(PropertySaveBehavior.Save);
entity.Property(task => task.UpdatedAt).Metadata
    .SetAfterSaveBehavior(PropertySaveBehavior.Save);
```

Import `Microsoft.EntityFrameworkCore.Metadata` only when needed. Do not switch to PostgreSQL `xmin`.

- [ ] **Step 3: Add cascade and partial-index tests**

Add a test that creates the complete graph, physically deletes its project, and asserts zero rows remain in tasks, assignees, task-labels, comments, and activity. Add this catalog query and assertion:

```csharp
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
Assert.All(definitions,
    definition => Assert.Contains("deleted_at IS NULL", definition));
```

- [ ] **Step 4: Correct the mapping workflow documentation**

In `database/README.md`, replace the claim that every business table must be scaffolded with:

```markdown
Sau khi migration tạo hoặc đổi bảng nghiệp vụ, cập nhật explicit EF Core
entities/mappings trong `TempoDesk.Infrastructure` và thêm integration test chạy
trên PostgreSQL thật. Repo hiện không dùng generated scaffold output cho business
entities. Chỉ dùng `database/scaffold.sh` như công cụ kiểm tra/tham khảo khi cần;
không commit output generated nếu chưa có một ticket chuyển toàn bộ persistence
sang workflow scaffold.
```

Keep the scaffold command as an optional reference.

- [ ] **Step 5: Run final verification**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj
dotnet build server/TempoDesk.sln
git diff --check
```

Expected: all tests pass, build reports zero warnings/errors, and `git diff --check` prints nothing.

If database credentials are available, also run:

```bash
dotnet run --project server/tools/TempoDesk.DatabaseMigrator
```

Expected: `APPLY 202611161730_create_project_task_schema.sql` on a new database or `SKIP` with matching checksum. If credentials are unavailable, record that omission; Testcontainers still runs every migration from an empty database.

- [ ] **Step 6: Commit the completed ticket**

```bash
git add server/tests/TempoDesk.Tests/WorkItems \
  server/src/TempoDesk.Infrastructure/WorkItems/Persistence/WorkItemPersistenceConfiguration.cs \
  database/README.md
git commit -m "test(database): verify task lifecycle and concurrency"
```

## Final Review Checklist

- [ ] `git status --short` contains no unintended files.
- [ ] SQL and EF names match character-for-character.
- [ ] No project-membership table/entity exists.
- [ ] No API, frontend, board, Jira, or unrelated refactor is included.
- [ ] Design and plan remain tracked under `/docs`.
