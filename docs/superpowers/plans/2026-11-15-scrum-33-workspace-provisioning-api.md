# SCRUM-33 Workspace Provisioning and API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Guarantee one Personal Workspace per account and add authenticated APIs to list, create, read, and rename workspaces.

**Architecture:** Application owns workspace contracts, validation outcomes, and transport-independent result records. Infrastructure implements provisioning and workspace operations with the existing `TempoDeskIdentityDbContext`; the API controller derives the caller from JWT claims and translates results to DTOs and Problem Details.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core 10, Npgsql 10, PostgreSQL 18, SQL-first migrations.

## Global Constraints

- Every account always has exactly one Personal Workspace named `Personal Workspace`.
- Registration does not ask the user to choose Personal or Team.
- A Personal Workspace cannot be converted or renamed.
- Team Workspace names are not unique.
- Workspace names are trimmed, required, and limited to 200 characters; Unicode is accepted.
- The acting user comes only from the authenticated `ClaimTypes.NameIdentifier` claim.
- Missing and inaccessible workspace IDs both return `404 Not Found`.
- Member and Viewer roles receive `403 Forbidden` when renaming a Team Workspace.
- Personal Workspace rename returns `409 Conflict` with `Personal Workspace names are managed by the system.`
- Successful responses return DTOs directly; errors use RFC Problem Details with `traceId`.
- Do not add invitations, member administration, centralized authorization policies, workspace deletion, branding, billing, projects, tasks, or a generic response envelope.
- Do not add a server test project in SCRUM-33; automated API, concurrency, and permission-matrix coverage belongs to SCRUM-37.

---

## File Structure

- Create `database/migrations/202611152300_backfill_personal_workspaces.sql`: backfill Personal Workspaces and Owner memberships for existing users.
- Create `server/src/TempoDesk.Application/Workspaces/IWorkspaceService.cs`: workspace operations.
- Create `server/src/TempoDesk.Application/Workspaces/WorkspaceSummary.cs`: application response record.
- Create `server/src/TempoDesk.Application/Workspaces/WorkspaceResult.cs`: explicit operation status and validation details.
- Create `server/src/TempoDesk.Application/Workspaces/WorkspaceName.cs`: shared name validation and normalization.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/PersonalWorkspaceProvisioner.cs`: idempotent PostgreSQL provisioning.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceService.cs`: EF-backed list/create/read/rename implementation.
- Modify `server/src/TempoDesk.Infrastructure/Identity/Registration/AccountRegistrationService.cs`: provision within registration and duplicate-account paths.
- Modify `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`: register provisioner and workspace service.
- Create `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceRequestDtos.cs`: create and rename request DTOs.
- Create `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceResponseDto.cs`: public workspace DTO.
- Create `server/src/TempoDesk.Api/Features/Workspaces/WorkspacesController.cs`: four authenticated endpoints and Problem Details translation.

### Task 1: Backfill Personal Workspaces for existing users

**Files:**

- Create: `database/migrations/202611152300_backfill_personal_workspaces.sql`

**Interfaces:**

- Consumes: `public.users`, `public.workspaces`, `public.workspace_members`, and SCRUM-32 constraints.
- Produces: exactly one Personal Workspace and active Owner membership for every user present when the migration runs.

- [ ] **Step 1: Verify the backfill migration is absent**

Run:

```bash
test ! -e database/migrations/202611152300_backfill_personal_workspaces.sql
```

Expected: exit code `0`.

- [ ] **Step 2: Create the forward migration**

Create `database/migrations/202611152300_backfill_personal_workspaces.sql`:

```sql
-- This migration only inserts missing Personal Workspaces and Owner
-- memberships. Rollback requires deleting only rows identified as backfill
-- data before those workspaces acquire product data. After release, use a new
-- forward migration instead of editing this file.

WITH missing_users AS (
    SELECT users.id AS user_id
    FROM public.users AS users
    WHERE NOT EXISTS (
        SELECT 1
        FROM public.workspaces AS workspace
        WHERE workspace.owner_user_id = users.id
          AND workspace.type = 'personal'
    )
),
inserted_workspaces AS (
    INSERT INTO public.workspaces (
        id,
        name,
        type,
        owner_user_id,
        created_at,
        updated_at
    )
    SELECT
        gen_random_uuid(),
        'Personal Workspace',
        'personal',
        missing_users.user_id,
        CURRENT_TIMESTAMP,
        CURRENT_TIMESTAMP
    FROM missing_users
    ON CONFLICT (owner_user_id) WHERE type = 'personal'
    DO NOTHING
    RETURNING id, owner_user_id, created_at
)
INSERT INTO public.workspace_members (
    id,
    workspace_id,
    user_id,
    role,
    created_at
)
SELECT
    gen_random_uuid(),
    inserted_workspaces.id,
    inserted_workspaces.owner_user_id,
    'owner',
    inserted_workspaces.created_at
FROM inserted_workspaces
ON CONFLICT (workspace_id, user_id) WHERE removed_at IS NULL
DO NOTHING;
```

- [ ] **Step 3: Apply the migration against PostgreSQL**

Run:

```bash
docker compose run --build --rm migrator
```

Expected: output contains
`APPLY 202611152300_backfill_personal_workspaces.sql` and exits `0`.

- [ ] **Step 4: Verify backfill invariants in the live database**

Run, substituting `.env`'s `POSTGRES_DB` value if it is not `tempodesk`:

```bash
docker compose exec -T postgres psql -U postgres -d tempodesk -v ON_ERROR_STOP=1 <<'SQL'
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM public.users AS users
    LEFT JOIN public.workspaces AS workspace
      ON workspace.owner_user_id = users.id
     AND workspace.type = 'personal'
    GROUP BY users.id
    HAVING count(workspace.id) <> 1
) THEN 1 / 0 ELSE 1 END AS every_user_has_one_personal_workspace;

SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM public.workspaces AS workspace
    LEFT JOIN public.workspace_members AS member
      ON member.workspace_id = workspace.id
     AND member.user_id = workspace.owner_user_id
     AND member.role = 'owner'
     AND member.removed_at IS NULL
    WHERE workspace.type = 'personal'
      AND member.id IS NULL
) THEN 1 / 0 ELSE 1 END AS every_personal_workspace_has_owner;
SQL
```

Expected: both rows contain `1` and the command exits `0`.

- [ ] **Step 5: Commit the migration**

```bash
git add database/migrations/202611152300_backfill_personal_workspaces.sql
git commit -m "feat(db): backfill personal workspaces"
```

### Task 2: Add application workspace contracts and name validation

**Files:**

- Create: `server/src/TempoDesk.Application/Workspaces/IWorkspaceService.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceSummary.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceResult.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceName.cs`

**Interfaces:**

- Produces: `IWorkspaceService`, `WorkspaceSummary`, `WorkspaceResult`, `WorkspaceResultStatus`, and `WorkspaceName.Normalize` for Tasks 4 and 5.

- [ ] **Step 1: Confirm the workspace application namespace is absent**

Run:

```bash
test ! -d server/src/TempoDesk.Application/Workspaces
```

Expected: exit code `0`.

- [ ] **Step 2: Add the application records and status enum**

Create `WorkspaceSummary.cs`:

```csharp
namespace TempoDesk.Application.Workspaces;

public sealed record WorkspaceSummary(
    Guid Id,
    string Name,
    string Type,
    string Role);
```

Create `WorkspaceResult.cs`:

```csharp
namespace TempoDesk.Application.Workspaces;

public enum WorkspaceResultStatus
{
    Success,
    InvalidName,
    NotFound,
    Forbidden,
    PersonalNameManaged
}

public sealed record WorkspaceResult(
    WorkspaceResultStatus Status,
    WorkspaceSummary? Workspace = null,
    string? ValidationError = null)
{
    public static WorkspaceResult Succeeded(WorkspaceSummary workspace) =>
        new(WorkspaceResultStatus.Success, workspace);

    public static WorkspaceResult Invalid(string error) =>
        new(WorkspaceResultStatus.InvalidName, ValidationError: error);

    public static WorkspaceResult Missing { get; } =
        new(WorkspaceResultStatus.NotFound);

    public static WorkspaceResult Denied { get; } =
        new(WorkspaceResultStatus.Forbidden);

    public static WorkspaceResult PersonalNameConflict { get; } =
        new(WorkspaceResultStatus.PersonalNameManaged);
}
```

- [ ] **Step 3: Add shared name normalization**

Create `WorkspaceName.cs`:

```csharp
namespace TempoDesk.Application.Workspaces;

public static class WorkspaceName
{
    public const int MaxLength = 200;
    public const string RequiredMessage = "Workspace name is required.";
    public const string TooLongMessage =
        "Workspace name must not exceed 200 characters.";

    public static string? Normalize(string? name, out string? error)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            error = RequiredMessage;
            return null;
        }

        if (normalized.Length > MaxLength)
        {
            error = TooLongMessage;
            return null;
        }

        error = null;
        return normalized;
    }
}
```

- [ ] **Step 4: Add the workspace service interface**

Create `IWorkspaceService.cs`:

```csharp
namespace TempoDesk.Application.Workspaces;

public interface IWorkspaceService
{
    Task<IReadOnlyList<WorkspaceSummary>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceResult> CreateTeamAsync(
        Guid userId,
        string? name,
        CancellationToken cancellationToken = default);

    Task<WorkspaceResult> GetAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceResult> RenameAsync(
        Guid userId,
        Guid workspaceId,
        string? name,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Build the Application project**

Run:

```bash
dotnet build server/src/TempoDesk.Application/TempoDesk.Application.csproj
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 6: Commit the contracts**

```bash
git add server/src/TempoDesk.Application/Workspaces
git commit -m "feat(workspaces): add application contracts"
```

### Task 3: Provision Personal Workspaces during registration

**Files:**

- Create: `server/src/TempoDesk.Infrastructure/Workspaces/PersonalWorkspaceProvisioner.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/Registration/AccountRegistrationService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`

**Interfaces:**

- Consumes: SCRUM-32 tables, `TempoDeskIdentityDbContext`, `TimeProvider`, and the existing registration transaction.
- Produces: `PersonalWorkspaceProvisioner.EnsureAsync(Guid, DateTimeOffset, CancellationToken)` and registration-time provisioning.

- [ ] **Step 1: Add the idempotent provisioner**

Create `PersonalWorkspaceProvisioner.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.Workspaces;

internal sealed class PersonalWorkspaceProvisioner(
    TempoDeskIdentityDbContext context)
{
    private const string PersonalWorkspaceName = "Personal Workspace";

    public async Task EnsureAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH target_workspace AS (
                INSERT INTO public.workspaces (
                    id, name, type, owner_user_id, created_at, updated_at
                )
                VALUES (
                    {workspaceId},
                    {PersonalWorkspaceName},
                    {WorkspaceTypeValues.Personal},
                    {userId},
                    {now},
                    {now}
                )
                ON CONFLICT (owner_user_id) WHERE type = 'personal'
                DO UPDATE SET owner_user_id = EXCLUDED.owner_user_id
                RETURNING id
            )
            INSERT INTO public.workspace_members (
                id, workspace_id, user_id, role, created_at
            )
            SELECT
                {membershipId},
                target_workspace.id,
                {userId},
                {WorkspaceRoleValues.Owner},
                {now}
            FROM target_workspace
            ON CONFLICT (workspace_id, user_id) WHERE removed_at IS NULL
            DO NOTHING
            """,
            cancellationToken);
    }
}
```

- [ ] **Step 2: Inject the provisioner into registration**

Update the `AccountRegistrationService` primary constructor to include:

```csharp
PersonalWorkspaceProvisioner personalWorkspaces,
```

Add:

```csharp
using TempoDesk.Infrastructure.Workspaces;
```

In the existing-user branch, ensure the workspace before resending or
returning:

```csharp
if (existing is not null)
{
    await personalWorkspaces.EnsureAsync(
        existing.Id,
        timeProvider.GetUtcNow(),
        cancellationToken);

    if (!existing.EmailConfirmed)
        await ResendVerificationAsync(email, cancellationToken);

    return RegistrationResult.Accepted;
}
```

After `users.CreateAsync` succeeds and before `AddToken`, add:

```csharp
await personalWorkspaces.EnsureAsync(user.Id, now, cancellationToken);
```

In the unique-violation catch, after loading `duplicate`, ensure its workspace
before resending:

```csharp
if (duplicate is not null)
{
    await personalWorkspaces.EnsureAsync(
        duplicate.Id,
        timeProvider.GetUtcNow(),
        cancellationToken);

    if (!duplicate.EmailConfirmed)
        await ResendVerificationAsync(email, cancellationToken);
}
```

- [ ] **Step 3: Register the provisioner**

Add this using to `IdentityConfiguration.cs`:

```csharp
using TempoDesk.Infrastructure.Workspaces;
```

Add this scoped registration before `IAccountRegistrationService`:

```csharp
services.AddScoped<PersonalWorkspaceProvisioner>();
```

- [ ] **Step 4: Build the Infrastructure project**

Run:

```bash
dotnet build server/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 5: Commit registration provisioning**

```bash
git add \
  server/src/TempoDesk.Infrastructure/Workspaces/PersonalWorkspaceProvisioner.cs \
  server/src/TempoDesk.Infrastructure/Identity/Registration/AccountRegistrationService.cs \
  server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs
git commit -m "feat(workspaces): provision personal workspaces"
```

### Task 4: Implement workspace operations in Infrastructure

**Files:**

- Create: `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`

**Interfaces:**

- Consumes: `IWorkspaceService`, application results, SCRUM-32 persistence entities, and `TimeProvider`.
- Produces: active-membership-scoped list/read, atomic Team creation, and role-aware rename.

- [ ] **Step 1: Create the Infrastructure workspace service**

Create `WorkspaceService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.Workspaces;

internal sealed class WorkspaceService(
    TempoDeskIdentityDbContext context,
    TimeProvider timeProvider) : IWorkspaceService
{
    public async Task<IReadOnlyList<WorkspaceSummary>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await context.WorkspaceMembers
            .AsNoTracking()
            .Where(member => member.UserId == userId &&
                member.RemovedAt == null)
            .OrderBy(member => member.Workspace.Type ==
                WorkspaceTypeValues.Personal ? 0 : 1)
            .ThenBy(member => member.Workspace.Name)
            .ThenBy(member => member.WorkspaceId)
            .Select(member => new WorkspaceSummary(
                member.WorkspaceId,
                member.Workspace.Name,
                member.Workspace.Type,
                member.Role))
            .ToListAsync(cancellationToken);

    public async Task<WorkspaceResult> CreateTeamAsync(
        Guid userId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        var normalized = WorkspaceName.Normalize(name, out var error);
        if (normalized is null)
            return WorkspaceResult.Invalid(error!);

        var now = timeProvider.GetUtcNow();
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = normalized,
            Type = WorkspaceTypeValues.Team,
            OwnerUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        var membership = new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceRoleValues.Owner,
            CreatedAt = now
        };

        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken);
        context.Workspaces.Add(workspace);
        context.WorkspaceMembers.Add(membership);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return WorkspaceResult.Succeeded(ToSummary(workspace, membership.Role));
    }

    public async Task<WorkspaceResult> GetAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var workspace = await AccessibleWorkspace(userId, workspaceId)
            .Select(member => new WorkspaceSummary(
                member.WorkspaceId,
                member.Workspace.Name,
                member.Workspace.Type,
                member.Role))
            .SingleOrDefaultAsync(cancellationToken);

        return workspace is null
            ? WorkspaceResult.Missing
            : WorkspaceResult.Succeeded(workspace);
    }

    public async Task<WorkspaceResult> RenameAsync(
        Guid userId,
        Guid workspaceId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        var normalized = WorkspaceName.Normalize(name, out var error);
        if (normalized is null)
            return WorkspaceResult.Invalid(error!);

        var membership = await AccessibleWorkspace(userId, workspaceId)
            .Include(member => member.Workspace)
            .SingleOrDefaultAsync(cancellationToken);
        if (membership is null)
            return WorkspaceResult.Missing;

        if (membership.Workspace.Type == WorkspaceTypeValues.Personal)
            return WorkspaceResult.PersonalNameConflict;

        if (membership.Role is not WorkspaceRoleValues.Owner and
            not WorkspaceRoleValues.Admin)
            return WorkspaceResult.Denied;

        membership.Workspace.Name = normalized;
        membership.Workspace.UpdatedAt = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);

        return WorkspaceResult.Succeeded(ToSummary(
            membership.Workspace,
            membership.Role));
    }

    private IQueryable<WorkspaceMember> AccessibleWorkspace(
        Guid userId,
        Guid workspaceId) => context.WorkspaceMembers.Where(member =>
            member.UserId == userId &&
            member.WorkspaceId == workspaceId &&
            member.RemovedAt == null);

    private static WorkspaceSummary ToSummary(
        Workspace workspace,
        string role) => new(
            workspace.Id,
            workspace.Name,
            workspace.Type,
            role);
}
```

- [ ] **Step 2: Register the workspace service**

Add:

```csharp
using TempoDesk.Application.Workspaces;
```

Then add after the provisioner registration:

```csharp
services.AddScoped<IWorkspaceService, WorkspaceService>();
```

- [ ] **Step 3: Build the Infrastructure project**

Run:

```bash
dotnet build server/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 4: Commit the workspace service**

```bash
git add \
  server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceService.cs \
  server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs
git commit -m "feat(workspaces): implement workspace operations"
```

### Task 5: Add authenticated workspace endpoints

**Files:**

- Create: `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceRequestDtos.cs`
- Create: `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceResponseDto.cs`
- Create: `server/src/TempoDesk.Api/Features/Workspaces/WorkspacesController.cs`

**Interfaces:**

- Consumes: `IWorkspaceService`, `WorkspaceResult`, and authenticated name-identifier claims.
- Produces: `GET /api/workspaces`, `POST /api/workspaces`, `GET /api/workspaces/{workspaceId}`, and `PATCH /api/workspaces/{workspaceId}`.

- [ ] **Step 1: Add request and response DTOs**

Create `WorkspaceRequestDtos.cs`:

```csharp
namespace TempoDesk.Api.Controllers;

public sealed record CreateWorkspaceRequestDto(string? Name);

public sealed record RenameWorkspaceRequestDto(string? Name);
```

Create `WorkspaceResponseDto.cs`:

```csharp
namespace TempoDesk.Api.Controllers;

public sealed record WorkspaceResponseDto(
    Guid Id,
    string Name,
    string Type,
    string Role);
```

- [ ] **Step 2: Add the authenticated controller**

Create `WorkspacesController.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Workspaces;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/workspaces")]
public sealed class WorkspacesController(
    IWorkspaceService workspaces) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        CancellationToken cancellationToken)
    {
        var result = await workspaces.ListAsync(
            UserId(),
            cancellationToken);
        return Ok(result.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateWorkspaceRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateName(request.Name, out var normalized);
        if (validation is not null)
            return validation;

        var result = await workspaces.CreateTeamAsync(
            UserId(),
            normalized,
            cancellationToken);
        if (result.Status != WorkspaceResultStatus.Success)
            return Failure(result);

        var response = ToDto(result.Workspace!);
        return CreatedAtAction(
            nameof(Read),
            new { workspaceId = response.Id },
            response);
    }

    [HttpGet("{workspaceId:guid}")]
    public async Task<IActionResult> Read(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await workspaces.GetAsync(
            UserId(),
            workspaceId,
            cancellationToken);
        return result.Status == WorkspaceResultStatus.Success
            ? Ok(ToDto(result.Workspace!))
            : Failure(result);
    }

    [HttpPatch("{workspaceId:guid}")]
    public async Task<IActionResult> Rename(
        Guid workspaceId,
        RenameWorkspaceRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateName(request.Name, out var normalized);
        if (validation is not null)
            return validation;

        var result = await workspaces.RenameAsync(
            UserId(),
            workspaceId,
            normalized,
            cancellationToken);
        return result.Status == WorkspaceResultStatus.Success
            ? Ok(ToDto(result.Workspace!))
            : Failure(result);
    }

    private IActionResult Failure(WorkspaceResult result)
    {
        switch (result.Status)
        {
            case WorkspaceResultStatus.InvalidName:
                ModelState.AddModelError(
                    "name",
                    result.ValidationError!);
                return ValidationProblem(ModelState);
            case WorkspaceResultStatus.NotFound:
                return NotFound();
            case WorkspaceResultStatus.Forbidden:
                return StatusCode(StatusCodes.Status403Forbidden);
            case WorkspaceResultStatus.PersonalNameManaged:
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail:
                        "Personal Workspace names are managed by the system.");
            default:
                throw new InvalidOperationException(
                    $"Unsupported workspace result: {result.Status}");
        }
    }

    private IActionResult? ValidateName(
        string? name,
        out string normalized)
    {
        var value = WorkspaceName.Normalize(name, out var error);
        normalized = value ?? "";
        if (value is not null)
            return null;

        ModelState.AddModelError("name", error!);
        return ValidationProblem(ModelState);
    }

    private Guid UserId() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static WorkspaceResponseDto ToDto(
        WorkspaceSummary workspace) => new(
            workspace.Id,
            workspace.Name,
            workspace.Type,
            workspace.Role);
}
```

- [ ] **Step 3: Build the complete server solution**

Run:

```bash
dotnet build server/TempoDesk.sln
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 4: Commit the endpoints**

```bash
git add server/src/TempoDesk.Api/Features/Workspaces
git commit -m "feat(api): add workspace endpoints"
```

### Task 6: Run live acceptance verification

**Files:**

- Verify only; modify SCRUM-33 files only if a check exposes a defect.

**Interfaces:**

- Consumes: Tasks 1–5 and the local Compose PostgreSQL/API stack.
- Produces: fresh migration, build, isolation, validation, and transaction evidence.

- [ ] **Step 1: Verify migration repeatability**

Run:

```bash
docker compose run --build --rm migrator
```

Expected: output contains
`SKIP  202611152300_backfill_personal_workspaces.sql` and exits `0`.

- [ ] **Step 2: Run the Release build**

```bash
dotnet build server/TempoDesk.sln --configuration Release
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 3: Start the API dependencies**

Run:

```bash
docker compose up -d --build postgres mailpit migrator api
```

Expected: `postgres`, `mailpit`, and `api` become healthy and `migrator`
completes successfully.

- [ ] **Step 4: Register and confirm two smoke-test accounts**

Run with local-only test credentials:

```bash
curl --fail-with-body -sS \
  -H 'Content-Type: application/json' \
  -d '{"email":"scrum33-a@example.test","password":"SmokePass123!Alpha"}' \
  http://localhost:5023/api/auth/register

curl --fail-with-body -sS \
  -H 'Content-Type: application/json' \
  -d '{"email":"scrum33-b@example.test","password":"SmokePass123!Bravo"}' \
  http://localhost:5023/api/auth/register

docker compose exec -T postgres psql -U postgres -d tempodesk -v ON_ERROR_STOP=1 <<'SQL'
UPDATE public.users
SET email_confirmed = true,
    updated_at = CURRENT_TIMESTAMP
WHERE normalized_email IN (
    'SCRUM33-A@EXAMPLE.TEST',
    'SCRUM33-B@EXAMPLE.TEST'
);

SELECT normalized_email, count(workspace.id)
FROM public.users AS users
JOIN public.workspaces AS workspace
  ON workspace.owner_user_id = users.id
 AND workspace.type = 'personal'
WHERE users.normalized_email IN (
    'SCRUM33-A@EXAMPLE.TEST',
    'SCRUM33-B@EXAMPLE.TEST'
)
GROUP BY normalized_email
HAVING count(workspace.id) = 1;
SQL
```

Expected: both registrations succeed and the query returns two rows with
count `1`.

- [ ] **Step 5: Obtain tokens and verify create/list/read/rename isolation**

Run:

```bash
scrum33_token_a=$(curl --fail-with-body -sS \
  -H 'Content-Type: application/json' \
  -d '{"email":"scrum33-a@example.test","password":"SmokePass123!Alpha"}' \
  http://localhost:5023/api/auth/login | jq -r '.accessToken')

scrum33_token_b=$(curl --fail-with-body -sS \
  -H 'Content-Type: application/json' \
  -d '{"email":"scrum33-b@example.test","password":"SmokePass123!Bravo"}' \
  http://localhost:5023/api/auth/login | jq -r '.accessToken')

scrum33_team=$(curl --fail-with-body -sS \
  -D /tmp/tempodesk-scrum33-create.headers \
  -H "Authorization: Bearer $scrum33_token_a" \
  -H 'Content-Type: application/json' \
  -d '{"name":"  Smoke Team  "}' \
  http://localhost:5023/api/workspaces)

scrum33_team_id=$(printf '%s' "$scrum33_team" | jq -r '.id')

rg -n '^HTTP/[^ ]+ 201' /tmp/tempodesk-scrum33-create.headers
rg -ni '^location: .*/api/workspaces/' \
  /tmp/tempodesk-scrum33-create.headers

curl --fail-with-body -sS \
  -H "Authorization: Bearer $scrum33_token_a" \
  http://localhost:5023/api/workspaces | \
  jq -e --arg id "$scrum33_team_id" \
    'any(.[]; .id == $id and .name == "Smoke Team" and .role == "owner")'

test "$(curl -sS -o /dev/null -w '%{http_code}' \
  -H "Authorization: Bearer $scrum33_token_b" \
  http://localhost:5023/api/workspaces/$scrum33_team_id)" = "404"

test "$(curl -sS -o /dev/null -w '%{http_code}' \
  -X PATCH \
  -H "Authorization: Bearer $scrum33_token_b" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Stolen"}' \
  http://localhost:5023/api/workspaces/$scrum33_team_id)" = "404"

curl --fail-with-body -sS \
  -H "Authorization: Bearer $scrum33_token_b" \
  http://localhost:5023/api/workspaces | \
  jq -e --arg id "$scrum33_team_id" \
    'all(.[]; .id != $id)'

curl --fail-with-body -sS \
  -X PATCH \
  -H "Authorization: Bearer $scrum33_token_a" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Renamed Team"}' \
  http://localhost:5023/api/workspaces/$scrum33_team_id | \
  jq -e '.name == "Renamed Team"'
```

Expected: all assertions exit `0`; user B receives `404` for both cross-user
operations.

Insert a Viewer membership for account B as a direct verification fixture,
then confirm an accessible-but-insufficient role receives `403`:

```bash
docker compose exec -T postgres psql -U postgres -d tempodesk \
  -v ON_ERROR_STOP=1 \
  -v workspace_id="$scrum33_team_id" <<'SQL'
INSERT INTO public.workspace_members (
    id, workspace_id, user_id, role, created_at
)
SELECT
    gen_random_uuid(),
    :'workspace_id'::uuid,
    users.id,
    'viewer',
    CURRENT_TIMESTAMP
FROM public.users AS users
WHERE users.normalized_email = 'SCRUM33-B@EXAMPLE.TEST';
SQL

test "$(curl -sS -o /dev/null -w '%{http_code}' \
  -X PATCH \
  -H "Authorization: Bearer $scrum33_token_b" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Viewer Rename"}' \
  http://localhost:5023/api/workspaces/$scrum33_team_id)" = "403"
```

Expected: the direct fixture insert succeeds and Viewer rename returns `403`.

- [ ] **Step 6: Verify validation and Personal rename conflict**

Run:

```bash
scrum33_personal_id=$(curl --fail-with-body -sS \
  -H "Authorization: Bearer $scrum33_token_a" \
  http://localhost:5023/api/workspaces | \
  jq -r '.[] | select(.type == "personal") | .id')

curl -sS \
  -X POST \
  -H "Authorization: Bearer $scrum33_token_a" \
  -H 'Content-Type: application/json' \
  -d '{"name":"   "}' \
  http://localhost:5023/api/workspaces | \
  jq -e '.status == 400 and .errors.name[0] == "Workspace name is required."'

jq -n '{name: ("x" * 201)}' | \
  curl -sS \
    -X POST \
    -H "Authorization: Bearer $scrum33_token_a" \
    -H 'Content-Type: application/json' \
    -d @- \
    http://localhost:5023/api/workspaces | \
  jq -e '.status == 400 and .errors.name[0] == "Workspace name must not exceed 200 characters."'

curl -sS \
  -X PATCH \
  -H "Authorization: Bearer $scrum33_token_a" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Private"}' \
  http://localhost:5023/api/workspaces/$scrum33_personal_id | \
  jq -e '.status == 409 and .detail == "Personal Workspace names are managed by the system."'
```

Expected: all three `jq` assertions exit `0`.

- [ ] **Step 7: Verify idempotent provisioning and database ownership**

Repeat registration for account A, then query counts:

```bash
curl --fail-with-body -sS \
  -H 'Content-Type: application/json' \
  -d '{"email":"scrum33-a@example.test","password":"SmokePass123!Alpha"}' \
  http://localhost:5023/api/auth/register

docker compose exec -T postgres psql -U postgres -d tempodesk -v ON_ERROR_STOP=1 <<'SQL'
SELECT CASE WHEN count(*) = 1 THEN 1 ELSE 1 / 0 END
FROM public.workspaces AS workspace
JOIN public.users AS users ON users.id = workspace.owner_user_id
WHERE users.normalized_email = 'SCRUM33-A@EXAMPLE.TEST'
  AND workspace.type = 'personal';

SELECT CASE WHEN count(*) = 1 THEN 1 ELSE 1 / 0 END
FROM public.workspace_members AS member
JOIN public.workspaces AS workspace ON workspace.id = member.workspace_id
JOIN public.users AS users ON users.id = member.user_id
WHERE users.normalized_email = 'SCRUM33-A@EXAMPLE.TEST'
  AND workspace.type = 'personal'
  AND member.role = 'owner'
  AND member.removed_at IS NULL;
SQL
```

Expected: both queries return `1`.

- [ ] **Step 8: Remove only smoke-test data**

Run:

```bash
docker compose exec -T postgres psql -U postgres -d tempodesk -v ON_ERROR_STOP=1 <<'SQL'
DELETE FROM public.users
WHERE normalized_email IN (
    'SCRUM33-A@EXAMPLE.TEST',
    'SCRUM33-B@EXAMPLE.TEST'
);
SQL

rm -f -- /tmp/tempodesk-scrum33-create.headers
```

Expected: two users are deleted and SCRUM-32 cascades remove their workspace
fixtures. This cleanup is recoverable only by rerunning the smoke flow.

- [ ] **Step 9: Check formatting and final scope**

Run:

```bash
git diff --check
git status --short
git diff --stat 3ece86d..HEAD
```

Confirm the diff contains only the migration, Application contracts,
Infrastructure provisioning/service/DI changes, API DTO/controller files, and
any focused correction required by the acceptance checks.

- [ ] **Step 10: Commit a verification fix only when required**

If verification exposed a defect, stage only its SCRUM-33 files and run:

```bash
git commit -m "fix(workspaces): satisfy SCRUM-33 acceptance checks"
```

If no correction was required, do not create an empty commit.
