# SCRUM-39 Project, Task, and Subtask APIs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add authorized, workspace-scoped project/task APIs, one-level subtasks, assignments, labels, comments, activity, optimistic concurrency, soft deletion/restoration, and timezone-aware My Work.

**Architecture:** Put transport-neutral commands, summaries, results, validation, and three service contracts in `TempoDesk.Application.WorkItems`. Implement those contracts directly with the existing EF Core context in `TempoDesk.Infrastructure.WorkItems`, and expose focused controllers in the API. Reuse workspace authorization, PostgreSQL constraints, the existing task query filter, and `TimeProvider`; add no repository, mediator, migration, or dependency.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core 10, Npgsql 10.0.3, PostgreSQL 18.6, xUnit 2.9.3, Testcontainers.PostgreSql 4.16.0, System.Text.Json.

## Global Constraints

- Scope every project/task query by `workspaceId`; inaccessible and cross-workspace IDs return `404`.
- Owner/Admin manage projects; Owner/Admin/Member manage tasks; Viewer is read-only.
- Support exactly one subtask level.
- Project archive uses existing `ArchivedAt`; task deletion/restoration uses existing `DeletedAt`.
- Require `If-Match: "<rowVersion>"` for task update, delete, and restore; stale versions return `409` with a safe `currentRowVersion`.
- Generate comment authorship and activity actors only from the authenticated principal.
- My Work requires an IANA `timeZone`, uses server `TimeProvider`, and returns `recentlyCompleted`, `overdue`, `today`, `upcoming`, `later`, and `unscheduled` without duplicates.
- Store timestamps as UTC; never timezone-convert `DateOnly` task fields.
- Do not add Jira sprint/backlog concepts, project membership, arbitrary-depth trees, project restore, comment editing/deletion, UI, E2E infrastructure, migration, or a new package.
- Follow TDD: write a focused failing test, verify the expected failure, implement only enough to pass, and rerun focused tests before each commit.

## File Map

- Create `server/src/TempoDesk.Application/WorkItems/WorkItemValues.cs`: shared status, priority, source, limits, and activity constants.
- Create `server/src/TempoDesk.Application/WorkItems/WorkItemModels.cs`: summaries, commands, presence-aware updates, generic result, and My Work groups.
- Create `server/src/TempoDesk.Application/WorkItems/WorkItemValidation.cs`: pure normalization and validation.
- Create `server/src/TempoDesk.Application/WorkItems/IProjectService.cs`, `ITaskService.cs`, and `IMyWorkService.cs`: use-case contracts.
- Move `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/WorkItemValues.cs` to Application and update consumers.
- Create `server/src/TempoDesk.Infrastructure/WorkItems/ProjectService.cs`, `TaskService.cs`, and `MyWorkService.cs`.
- Modify `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`: register the services.
- Modify `server/src/TempoDesk.Application/Workspaces/WorkspaceAction.cs` and `WorkspacePermissionPolicy.cs`: work-item permissions.
- Create `server/src/TempoDesk.Api/Features/WorkItems/WorkItemRequestDtos.cs`, `WorkItemResponseDtos.cs`, and `WorkItemControllerSupport.cs`.
- Create `server/src/TempoDesk.Api/Features/WorkItems/ProjectsController.cs`, `TasksController.cs`, `TaskDetailsController.cs`, and `MyWorkController.cs`.
- Create `server/tests/TempoDesk.Tests/WorkItems/WorkItemScenario.cs`: shared PostgreSQL scenario builder.
- Create `server/tests/TempoDesk.Tests/WorkItems/WorkItemValidationTests.cs`, `WorkItemPermissionTests.cs`, `ProjectServiceTests.cs`, `TaskServiceTests.cs`, `TaskCollaborationTests.cs`, `MyWorkServiceTests.cs`, and `WorkItemApiTests.cs`.
- Modify existing project/task persistence tests to import the moved Application constants.

---

### Task 1: Application contracts, validation, and permission policy

**Files:**
- Create: `server/src/TempoDesk.Application/WorkItems/WorkItemValues.cs`
- Create: `server/src/TempoDesk.Application/WorkItems/WorkItemModels.cs`
- Create: `server/src/TempoDesk.Application/WorkItems/WorkItemValidation.cs`
- Create: `server/src/TempoDesk.Application/WorkItems/IProjectService.cs`
- Create: `server/src/TempoDesk.Application/WorkItems/ITaskService.cs`
- Create: `server/src/TempoDesk.Application/WorkItems/IMyWorkService.cs`
- Modify: `server/src/TempoDesk.Application/Workspaces/WorkspaceAction.cs`
- Modify: `server/src/TempoDesk.Application/Workspaces/WorkspacePermissionPolicy.cs`
- Modify: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/WorkTask.cs`
- Delete: `server/src/TempoDesk.Infrastructure/WorkItems/Persistence/WorkItemValues.cs`
- Modify: `server/tests/TempoDesk.Tests/WorkItems/ProjectTaskPersistenceTests.cs`
- Modify: `server/tests/TempoDesk.Tests/WorkItems/ProjectTaskSchemaTests.cs`
- Test: `server/tests/TempoDesk.Tests/WorkItems/WorkItemValidationTests.cs`
- Test: `server/tests/TempoDesk.Tests/WorkItems/WorkItemPermissionTests.cs`

**Interfaces:**
- Consumes: existing `WorkspaceRoles`, SCRUM-38 value strings, `DateOnly`, and `TimeZoneInfo`.
- Produces: `FieldUpdate<T>`, `WorkItemResult<T>`, summaries/commands, and the three service interfaces used by later tasks.

- [ ] **Step 1: Write failing permission and validation tests**

```csharp
[Theory]
[InlineData(WorkspaceRoles.Owner, WorkspaceAction.ManageProjects, true)]
[InlineData(WorkspaceRoles.Admin, WorkspaceAction.ManageProjects, true)]
[InlineData(WorkspaceRoles.Member, WorkspaceAction.ManageProjects, false)]
[InlineData(WorkspaceRoles.Viewer, WorkspaceAction.ManageProjects, false)]
[InlineData(WorkspaceRoles.Member, WorkspaceAction.ManageTasks, true)]
[InlineData(WorkspaceRoles.Viewer, WorkspaceAction.ManageTasks, false)]
[InlineData(WorkspaceRoles.Viewer, WorkspaceAction.ViewWorkItems, true)]
public void WorkItemActionsFollowMatrix(
    string role, WorkspaceAction action, bool expected) =>
    Assert.Equal(expected, WorkspacePermissionPolicy.Allows(role, action));

[Fact]
public void TaskValidationReturnsFieldErrors()
{
    var errors = WorkItemValidation.Task(new TaskMutation(
        " ", null, "invalid", "invalid",
        new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 17),
        0, null));
    Assert.Contains("title", errors.Keys);
    Assert.Contains("priority", errors.Keys);
    Assert.Contains("status", errors.Keys);
    Assert.Contains("dueDate", errors.Keys);
    Assert.Contains("estimatedMinutes", errors.Keys);
}

[Theory]
[InlineData(" Backend ", "Backend", "BACKEND")]
[InlineData("backend", "backend", "BACKEND")]
public void LabelNormalizationIsCaseInsensitive(
    string input, string display, string normalized)
{
    var result = WorkItemValidation.Label(input);
    Assert.Equal(display, result.Value);
    Assert.Equal(normalized, result.NormalizedValue);
    Assert.Empty(result.Errors);
}
```

- [ ] **Step 2: Run focused tests and verify red**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter 'FullyQualifiedName~WorkItemPermissionTests|FullyQualifiedName~WorkItemValidationTests'
```

Expected: FAIL to compile because the new actions, models, and validator do not exist.

- [ ] **Step 3: Add shared constants, models, and service contracts**

Define these exact core shapes:

```csharp
public readonly record struct FieldUpdate<T>(bool IsSpecified, T? Value);

public enum WorkItemResultStatus
{
    Success, Invalid, NotFound, Forbidden, Conflict
}

public sealed record WorkItemResult<T>(
    WorkItemResultStatus Status,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    long? CurrentRowVersion = null);

public sealed record ProjectSummary(
    Guid Id, Guid WorkspaceId, string Name, string? Description,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record AssigneeSummary(Guid UserId, string Email);
public sealed record LabelSummary(Guid Id, string Name);

public sealed record TaskSummary(
    Guid Id, Guid WorkspaceId, Guid ProjectId, Guid? ParentTaskId,
    string Title, string? Description, string Priority, string Status,
    DateOnly? StartDate, DateOnly? DueDate, int? EstimatedMinutes,
    string SourceType, decimal SortKey, long RowVersion,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<AssigneeSummary> Assignees,
    IReadOnlyList<LabelSummary> Labels);

public sealed record TaskMutation(
    string? Title, string? Description, string? Priority, string? Status,
    DateOnly? StartDate, DateOnly? DueDate, int? EstimatedMinutes,
    Guid? ParentTaskId);

public sealed record TaskPatch(
    FieldUpdate<string> Title,
    FieldUpdate<string?> Description,
    FieldUpdate<string> Priority,
    FieldUpdate<string> Status,
    FieldUpdate<DateOnly?> StartDate,
    FieldUpdate<DateOnly?> DueDate,
    FieldUpdate<int?> EstimatedMinutes,
    FieldUpdate<Guid?> ParentTaskId);
```

Also define `TaskCommentSummary`, `TaskActivitySummary` (with `JsonElement Details`), and `MyWorkSummary` with the six approved task arrays. Preserve the existing status/priority/source class names while moving them to Application. Add `WorkItemLimits` (`200`, `300`, `100`) and activity constants: `task_created`, `task_updated`, `assignees_changed`, `labels_changed`, `comment_added`, `task_deleted`, `task_restored`.

`IProjectService` exposes list/create/get/update/archive. `ITaskService` exposes list/create/get/update/delete/restore, replace assignees/labels, list/add comments, and list activity; update/delete/restore accept `long rowVersion`. `IMyWorkService.GetAsync(Guid actorUserId, string? timeZone, CancellationToken)` returns `WorkItemResult<MyWorkSummary>`.

- [ ] **Step 4: Implement pure validation and move constants**

Implement `WorkItemValidation` without EF dependencies. Trim project/task/label/comment text, normalize labels with `ToUpperInvariant()`, validate exact closed value sets, reject `DueDate < StartDate`, and reject non-positive estimates. Return field-keyed errors; never throw for user input.

Move the existing value classes without renaming members. Update persistence/tests to import `TempoDesk.Application.WorkItems`. Add `ViewWorkItems`, `ManageProjects`, and `ManageTasks` to the workspace action/policy using the tested matrix.

- [ ] **Step 5: Verify green and commit**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter 'FullyQualifiedName~WorkItemPermissionTests|FullyQualifiedName~WorkItemValidationTests|FullyQualifiedName~ProjectTaskPersistenceTests|FullyQualifiedName~ProjectTaskSchemaTests'
git add server/src/TempoDesk.Application/WorkItems \
  server/src/TempoDesk.Application/Workspaces \
  server/src/TempoDesk.Infrastructure/WorkItems/Persistence \
  server/tests/TempoDesk.Tests/WorkItems
git commit -m "feat(application): define work item use cases"
```

Expected: tests PASS; moving constants does not alter SQL/EF behavior.

---

### Task 2: Authorized project service and API

**Files:**
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/ProjectService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Create: `server/src/TempoDesk.Api/Features/WorkItems/WorkItemRequestDtos.cs`
- Create: `server/src/TempoDesk.Api/Features/WorkItems/WorkItemResponseDtos.cs`
- Create: `server/src/TempoDesk.Api/Features/WorkItems/WorkItemControllerSupport.cs`
- Create: `server/src/TempoDesk.Api/Features/WorkItems/ProjectsController.cs`
- Create: `server/tests/TempoDesk.Tests/WorkItems/WorkItemScenario.cs`
- Create: `server/tests/TempoDesk.Tests/WorkItems/ProjectServiceTests.cs`
- Create: `server/tests/TempoDesk.Tests/WorkItems/WorkItemApiTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts, workspace authorization, context, and `TimeProvider`.
- Produces: working project routes and a reusable integration-test scenario.

- [ ] **Step 1: Write failing lifecycle, isolation, PATCH, and API tests**

```csharp
[Theory]
[InlineData(WorkspaceRoles.Owner, true)]
[InlineData(WorkspaceRoles.Admin, true)]
[InlineData(WorkspaceRoles.Member, false)]
[InlineData(WorkspaceRoles.Viewer, false)]
public async Task CreateProjectFollowsPermissionMatrix(string role, bool allowed);

[Fact] public async Task ListAndReadExcludeArchivedProjects();
[Fact] public async Task CrossWorkspaceProjectIdReturnsNotFound();
[Fact] public async Task PatchDistinguishesOmittedDescriptionFromExplicitNull();
```

Build `WorkItemScenario` with Owner, Admin, Member, Viewer, unrelated user, Team Workspace, and Personal Workspace. API tests authenticate with `ClaimTypes.NameIdentifier`, assert `[Authorize]`, assert `CreatedAtActionResult`, `403`, `404`, and verify only approved project fields serialize.

- [ ] **Step 2: Run project tests and verify red**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter 'FullyQualifiedName~ProjectServiceTests|FullyQualifiedName~WorkItemApiTests'
```

Expected: FAIL because the service, DTOs, and controller do not exist.

- [ ] **Step 3: Implement project service and controller**

Authorize list/read with `ViewWorkItems`; create/update/archive with `ManageProjects`. Every query includes route workspace and `ArchivedAt == null`. Reads use `AsNoTracking()`. Create/update/archive timestamps come from `TimeProvider.GetUtcNow()`.

Use `JsonElement?` for PATCH fields: absent means `FieldUpdate.IsSpecified == false`; explicit JSON null clears description but is invalid for name. Controller route is `[Route("api/workspaces/{workspaceId:guid}/projects")]`; map invalid/forbidden/missing to `400/403/404`, create to `201`, and success to safe DTOs.

- [ ] **Step 4: Register, verify green, and commit**

Add `services.AddScoped<IProjectService, ProjectService>();`, then run:

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter 'FullyQualifiedName~ProjectServiceTests|FullyQualifiedName~WorkItemApiTests'
git add server/src/TempoDesk.Infrastructure/WorkItems/ProjectService.cs \
  server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs \
  server/src/TempoDesk.Api/Features/WorkItems \
  server/tests/TempoDesk.Tests/WorkItems
git commit -m "feat(api): add project management"
```

Expected: PASS without database exception output.

---

### Task 3: Task CRUD, nesting, concurrency, and soft lifecycle

**Files:**
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/TaskService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Modify: `server/src/TempoDesk.Api/Features/WorkItems/WorkItemRequestDtos.cs`
- Modify: `server/src/TempoDesk.Api/Features/WorkItems/WorkItemResponseDtos.cs`
- Modify: `server/src/TempoDesk.Api/Features/WorkItems/WorkItemControllerSupport.cs`
- Create: `server/src/TempoDesk.Api/Features/WorkItems/TasksController.cs`
- Create: `server/tests/TempoDesk.Tests/WorkItems/TaskServiceTests.cs`
- Modify: `server/tests/TempoDesk.Tests/WorkItems/WorkItemApiTests.cs`

**Interfaces:**
- Consumes: Task 1 `ITaskService`, Task 2 API support/scenario, and SCRUM-38 query filter/concurrency trigger.
- Produces: list/create/read/PATCH/delete/restore routes and the base task service extended by Task 4.

- [ ] **Step 1: Write failing task tests**

```csharp
[Fact] public async Task MemberCreatesParentAndOneLevelSubtask();
[Fact] public async Task ViewerCannotCreateOrUpdateTask();
[Fact] public async Task ParentFromAnotherProjectOrSubtaskIsInvalid();
[Fact] public async Task TaskUnderArchivedProjectIsNotFound();
[Fact] public async Task StaleUpdateReturnsConflictWithoutOverwriting();
[Fact] public async Task DeleteAndRestoreAdvanceRowVersion();
[Fact] public async Task DeletingParentSoftDeletesActiveChildren();
[Fact] public async Task RestoringParentDoesNotRestorePreviouslyDeletedChild();
[Fact] public async Task CrossWorkspaceTaskIdReturnsNotFound();
```

API tests assert missing/malformed `If-Match` gives `400`, stale version gives `409` plus `currentRowVersion`, success sets `ETag`, and JSON exposes no EF navigations/internal user fields.

- [ ] **Step 2: Run focused tests and verify red**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter 'FullyQualifiedName~TaskServiceTests|FullyQualifiedName~WorkItemApiTests'
```

Expected: FAIL because task service/controller and ETag handling are absent.

- [ ] **Step 3: Implement reads, create, and PATCH**

Authorize reads with `ViewWorkItems`, mutations with `ManageTasks`. Normal queries require route workspace, unarchived project, and the active-task filter. Creation validates project/parent, rejects a parent that is already a subtask, uses source `manual`, chooses `max(sort_key) + 1000` in the same project/parent scope, and inserts `task_created` atomically.

PATCH applies only specified fields, validates the complete resulting state and specified parent, assigns the submitted EF original concurrency value, and records exact changed fields in `task_updated`. Use one stable activity payload contract: creation stores `{ "status": "todo" }` (or the actual initial status), and updates store `{ "changes": { "status": { "from": "todo", "to": "done" } } }` plus the same `from`/`to` shape for every changed field. This exact status shape is the source for My Work completion time. A no-op returns without activity/version advancement. Catch only `DbUpdateConcurrencyException`; clear failed tracking, safely reload by workspace and active project, and return `Conflict` with current version or `NotFound`.

- [ ] **Step 4: Implement approved delete/restore cascade**

Delete queries active children directly, checks parent version, chooses a timestamp later than prior deletion activity when the clock has not advanced, writes it to every affected `DeletedAt`, and records `cascadeRootTaskId`. Restore uses `IgnoreQueryFilters()` and restores only children with both the current deletion timestamp and the parent's cascade marker. Write deletion/restoration activity per affected task in the same transaction.

- [ ] **Step 5: Add transport, ETag parsing, register, and verify**

`TryParseIfMatch` accepts exactly one quoted positive `long`; reject weak ETags, wildcards, multiple values, zero, negative, and unquoted values. Create ticket routes for task list/create/read/PATCH/delete/restore; DELETE/restore use no body.

Add `services.AddScoped<ITaskService, TaskService>();`, then run the Step 2 command. Expected: PASS.

- [ ] **Step 6: Commit task lifecycle**

```bash
git add server/src/TempoDesk.Infrastructure/WorkItems/TaskService.cs \
  server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs \
  server/src/TempoDesk.Api/Features/WorkItems \
  server/tests/TempoDesk.Tests/WorkItems
git commit -m "feat(api): add task lifecycle"
```

---

### Task 4: Assignees, labels, comments, and immutable activity

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/WorkItems/TaskService.cs`
- Modify: `server/src/TempoDesk.Api/Features/WorkItems/WorkItemRequestDtos.cs`
- Modify: `server/src/TempoDesk.Api/Features/WorkItems/WorkItemResponseDtos.cs`
- Create: `server/src/TempoDesk.Api/Features/WorkItems/TaskDetailsController.cs`
- Create: `server/tests/TempoDesk.Tests/WorkItems/TaskCollaborationTests.cs`
- Modify: `server/tests/TempoDesk.Tests/WorkItems/WorkItemApiTests.cs`

**Interfaces:**
- Consumes: Task 3's safe task loader and transaction/activity conventions.
- Produces: complete assignee, label, comment, and activity endpoints.

- [ ] **Step 1: Write failing collaboration tests**

```csharp
[Fact] public async Task AssigneesMustBeDistinctActiveWorkspaceMembers();
[Fact] public async Task CrossWorkspaceAssigneeIsInvalidWithoutLeakingIdentity();
[Fact] public async Task LabelNamesReuseCaseInsensitiveWorkspaceLabel();
[Fact] public async Task ConcurrentLabelCreationProducesOneLabel();
[Fact] public async Task CommentAuthorComesFromAuthenticatedActor();
[Fact] public async Task ViewerCanListButCannotMutateDetails();
[Fact] public async Task ActivityContainsServerKnownActorAndEventDetails();
[Fact] public async Task FailedRelationshipMutationRollsBackActivity();
```

API tests prove request DTOs contain no actor/activity fields and responses expose only approved IDs, email, label, comment, and activity data.

- [ ] **Step 2: Run focused tests and verify red**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter 'FullyQualifiedName~TaskCollaborationTests|FullyQualifiedName~WorkItemApiTests'
```

Expected: FAIL because collaboration methods/controller are missing.

- [ ] **Step 3: Implement assignee and label replacement**

Begin a transaction, lock the workspace row using the membership service's lock order, then authorize and validate active members. Distinct assignee IDs, replace links, and add `assignees_changed` with ordered added/removed IDs. The workspace lock serializes concurrent full-set replacements; reload the response before commit. No-op replacements write nothing.

Validate/normalize all label names first. Load labels by normalized names, create missing rows, replace links, and add `labels_changed` with ordered added/removed label IDs. On PostgreSQL unique violation specifically for `ux_labels_workspace_normalized_name`, roll back that attempt, clear failed added label entries, reload winning labels, and retry once in a fresh transaction. Do not catch unrelated violations.

- [ ] **Step 4: Implement comments, activity reads, and endpoints**

Create comments with `AuthorUserId = actorUserId`; create `comment_added` containing only `commentId`; commit both atomically. List comments/activity oldest-first by `(CreatedAt, Id)` with author/actor email. Never accept actor, author, event type, or details in a request.

Add the five ticket endpoints under an authorized task-details controller: replace assignees, replace labels, list comments, add comment, and list activity. Reads use `ViewWorkItems`; mutations use `ManageTasks`. Return replacements as `200`, comments as `201`, validation as `400`, insufficient active role as `403`, and inaccessible task as `404`.

- [ ] **Step 5: Verify green and commit**

Run the Step 2 command. Expected: PASS. Then:

```bash
git add server/src/TempoDesk.Infrastructure/WorkItems/TaskService.cs \
  server/src/TempoDesk.Api/Features/WorkItems \
  server/tests/TempoDesk.Tests/WorkItems
git commit -m "feat(api): add task collaboration history"
```

---

### Task 5: Timezone-aware My Work

**Files:**
- Create: `server/src/TempoDesk.Infrastructure/WorkItems/MyWorkService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Create: `server/src/TempoDesk.Api/Features/WorkItems/MyWorkController.cs`
- Modify: `server/src/TempoDesk.Api/Features/WorkItems/WorkItemResponseDtos.cs`
- Create: `server/tests/TempoDesk.Tests/WorkItems/MyWorkServiceTests.cs`
- Modify: `server/tests/TempoDesk.Tests/WorkItems/WorkItemApiTests.cs`

**Interfaces:**
- Consumes: `IMyWorkService`, memberships, task/assignee/activity data, and `TimeProvider`.
- Produces: `GET /api/my-work?timeZone=...` with six exclusive groups.

- [ ] **Step 1: Write failing scope, timezone, and grouping tests**

Use fixed time `2026-11-17T01:00:00Z`:

```csharp
[Theory]
[InlineData("Asia/Shanghai", "2026-11-17")]
[InlineData("America/Los_Angeles", "2026-11-16")]
public async Task TimeZoneDeterminesLocalToday(string zone, string expectedDate);

[Fact] public async Task PersonalWorkspaceIncludesAllCallerTasks();
[Fact] public async Task TeamWorkspaceIncludesOnlyAssignedCallerTasks();
[Fact] public async Task RemovedMembershipAndArchivedOrDeletedWorkAreExcluded();
[Fact] public async Task SixGroupsAreExclusiveAndContainEveryActiveTask();
[Fact] public async Task RecentlyCompletedUsesLatestDoneActivity();
[Fact] public async Task ReopenedTaskIsNotRecentlyCompleted();
[Fact] public async Task LosAngelesDstBoundaryUsesCorrectLocalDate();
[Theory]
[InlineData(null)]
[InlineData("")]
[InlineData("Not/AZone")]
public async Task InvalidTimeZoneReturnsValidation(string? zone);
```

- [ ] **Step 2: Run My Work tests and verify red**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter 'FullyQualifiedName~MyWorkServiceTests|FullyQualifiedName~WorkItemApiTests'
```

Expected: FAIL because My Work service/controller are absent.

- [ ] **Step 3: Implement timezone and authorized candidate query**

Resolve via `TimeZoneInfo.FindSystemTimeZoneById`; convert `TimeProvider.GetUtcNow()` with `TimeZoneInfo.ConvertTime`; use `DateOnly.FromDateTime(local.DateTime)`. Resolve local-date UTC boundaries by advancing invalid midnight wall times to the first valid instant and choosing the earliest instant when ambiguous. Map missing/unknown/invalid zones to a `timeZone` validation error.

Query incomplete authorized tasks plus completed candidates whose create/update activity falls in a bounded UTC range covering the seven local dates. Use split collection queries. Then query only bounded activity for those materialized task IDs and inspect the stable Task 3 JSON contract to find the latest creation-as-`done` or transition-to-`done` event. This shape keeps JSON parsing simple without loading inaccessible or unbounded completed history.

- [ ] **Step 4: Group once with approved precedence**

```csharp
if (task.Status == WorkItemStatusValues.Done)
{
    if (completedDate >= today.AddDays(-6) && completedDate <= today)
        recentlyCompleted.Add(task);
    continue;
}
if (task.DueDate is null) unscheduled.Add(task);
else if (task.DueDate < today) overdue.Add(task);
else if (task.DueDate == today) todayItems.Add(task);
else if (task.DueDate <= today.AddDays(7)) upcoming.Add(task);
else later.Add(task);
```

Order deterministically by due date where present, then project/task display data, then task ID. Exclude a completed task without a trustworthy completion event rather than guessing from `UpdatedAt`.

- [ ] **Step 5: Add endpoint, register, verify, and commit**

Add `services.AddScoped<IMyWorkService, MyWorkService>();`. Create authorized `GET /api/my-work` taking `[FromQuery] string? timeZone`; map validation to Problem Details and success to six camel-case arrays.

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter 'FullyQualifiedName~MyWorkServiceTests|FullyQualifiedName~WorkItemApiTests'
git add server/src/TempoDesk.Infrastructure/WorkItems/MyWorkService.cs \
  server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs \
  server/src/TempoDesk.Api/Features/WorkItems \
  server/tests/TempoDesk.Tests/WorkItems
git commit -m "feat(api): add timezone aware my work"
```

Expected: PASS, including China/US and DST cases.

---

### Task 6: Acceptance matrix and final verification

**Files:**
- Modify: all new `server/tests/TempoDesk.Tests/WorkItems/*ServiceTests.cs`
- Modify: `server/tests/TempoDesk.Tests/WorkItems/WorkItemApiTests.cs`

**Interfaces:**
- Consumes: complete SCRUM-39 implementation.
- Produces: acceptance evidence for permissions, isolation, conflict, lifecycle, activity, and My Work.

- [ ] **Step 1: Complete role and tenant matrix tests**

Use theories for Owner/Admin/Member/Viewer across view, project mutation, task mutation, assignment/label mutation, and comment creation. Add removed-member/unrelated-user cases. Cover foreign project, task, parent, assignee, and label IDs. Expect `Forbidden` only for an active member with insufficient role and `NotFound` for missing membership/resource.

- [ ] **Step 2: Run WorkItems tests and close only observed gaps**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj \
  --filter FullyQualifiedName~TempoDesk.Tests.WorkItems
```

Expected: newly added uncovered rows fail for a specific reason; fix production code only, then rerun to PASS.

- [ ] **Step 3: Verify API metadata and safe Problem Details**

Add reflection/serialization assertions that all controllers have `[Authorize]`, route templates match the ticket, requests cannot set actor/history, responses expose no password/security/concurrency-stamp fields, and invalid/forbidden/missing/conflict results map to `400/403/404/409` with `ValidationProblemDetails` or `ProblemDetails`.

- [ ] **Step 4: Run full verification**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj
dotnet build server/TempoDesk.sln
git diff --check
git status --short
```

Expected: all tests PASS; build has zero warnings/errors; diff check is empty; status contains only intended SCRUM-39 files.

- [ ] **Step 5: Run migrator when credentials are configured**

```bash
dotnet run --project server/tools/TempoDesk.DatabaseMigrator
```

Expected: existing migrations report `SKIP` with matching checksums. If credentials are unavailable, record the omission; Testcontainers already validates clean-database migrations and runtime queries.

- [ ] **Step 6: Commit acceptance completion**

```bash
git add server/tests/TempoDesk.Tests/WorkItems \
  server/src/TempoDesk.Application/WorkItems \
  server/src/TempoDesk.Application/Workspaces \
  server/src/TempoDesk.Infrastructure/WorkItems \
  server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs \
  server/src/TempoDesk.Api/Features/WorkItems
git commit -m "test(api): verify SCRUM-39 acceptance"
```

## Final Review Checklist

- [ ] All 17 ticket routes exist and require authentication.
- [ ] Project/task access is workspace-scoped before resource details are returned.
- [ ] Tests cover Owner, Admin, Member, Viewer, removed members, and unrelated users.
- [ ] One-level nesting, active-member assignments, normalized labels, immutable comments/activity, and actor provenance are enforced.
- [ ] Task update/delete/restore require current row versions; stale writes do not overwrite data.
- [ ] Parent delete/restore preserves timestamp-and-cascade-marker child behavior.
- [ ] My Work handles IANA zones, DST, six exclusive groups, Personal/Team scope, and no duplicates.
- [ ] No migration, dependency, sprint/backlog feature, project membership, UI, or unrelated refactor was added.
- [ ] Full tests, build, and `git diff --check` pass.
