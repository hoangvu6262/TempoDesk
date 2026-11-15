# SCRUM-34 Workspace Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centralize workspace role authorization so active database membership and one fixed backend permission matrix govern every existing workspace resource operation.

**Architecture:** The Application project defines stable roles, actions, a pure permission policy, and the authorization contract. Infrastructure loads active memberships, evaluates the shared policy, and workspace operations consume that boundary while preserving non-leaking `404` behavior.

**Tech Stack:** .NET 10, C# 14, ASP.NET Core, EF Core 10, PostgreSQL.

## Global Constraints

- Caller identity comes only from the authenticated `NameIdentifier` claim.
- Missing, removed, and cross-workspace memberships all produce the same `NotFound` authorization result.
- Persisted unknown roles and undefined actions fail closed.
- `POST /api/workspaces` remains account-scoped and does not require an existing workspace permission.
- Do not add SCRUM-35 membership endpoints, frontend UI, custom roles, project roles, or organization administrators.
- Do not restore server test projects; SCRUM-37 owns the minimum Sprint 2 test infrastructure and automated authorization coverage.
- Verify this ticket with `dotnet build server/TempoDesk.sln`.

## File Structure

- `server/src/TempoDesk.Application/Workspaces/WorkspaceRoles.cs` — stable persisted role values shared across layers.
- `server/src/TempoDesk.Application/Workspaces/WorkspaceAction.cs` — stable action vocabulary matching the SCRUM-34 matrix.
- `server/src/TempoDesk.Application/Workspaces/WorkspacePermissionPolicy.cs` — pure fail-closed role/action evaluation.
- `server/src/TempoDesk.Application/Workspaces/WorkspaceAuthorizationResult.cs` — allowed/not-found/forbidden result contract.
- `server/src/TempoDesk.Application/Workspaces/IWorkspaceAuthorizationService.cs` — reusable resource authorization boundary.
- `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceAuthorizationService.cs` — active-membership lookup and policy evaluation.
- `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceService.cs` — route list/read/rename behavior through the shared policy.
- `server/src/TempoDesk.Infrastructure/Workspaces/PersonalWorkspaceProvisioner.cs` — consume the Application role constant.
- `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs` — register the authorization service.
- `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceRoleValues.cs` — remove after moving stable values to Application.

---

### Task 1: Define the stable permission model

**Files:**
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceRoles.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceAction.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspacePermissionPolicy.cs`

**Interfaces:**
- Consumes: persisted lowercase role strings already used by the workspace schema.
- Produces: `WorkspaceRoles`, `WorkspaceAction`, and `WorkspacePermissionPolicy.Allows(string role, WorkspaceAction action)`.

- [ ] **Step 1: Add the stable role values**

Create `WorkspaceRoles.cs`:

```csharp
namespace TempoDesk.Application.Workspaces;

public static class WorkspaceRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";
    public const string Viewer = "viewer";
}
```

- [ ] **Step 2: Add the stable action vocabulary**

Create `WorkspaceAction.cs`:

```csharp
namespace TempoDesk.Application.Workspaces;

public enum WorkspaceAction
{
    ViewWorkspace,
    RenameWorkspace,
    ViewMembers,
    InviteMemberOrViewer,
    InviteAdmin,
    ChangeMemberOrViewerRole,
    ChangeAdmin,
    RemoveMemberOrViewer,
    RemoveAdmin,
    TransferOwnership,
    LeaveWorkspace
}
```

- [ ] **Step 3: Add the fixed fail-closed permission matrix**

Create `WorkspacePermissionPolicy.cs`:

```csharp
namespace TempoDesk.Application.Workspaces;

public static class WorkspacePermissionPolicy
{
    public static bool Allows(string role, WorkspaceAction action) =>
        action switch
        {
            WorkspaceAction.ViewWorkspace or
            WorkspaceAction.ViewMembers => role is
                WorkspaceRoles.Owner or
                WorkspaceRoles.Admin or
                WorkspaceRoles.Member or
                WorkspaceRoles.Viewer,

            WorkspaceAction.RenameWorkspace or
            WorkspaceAction.InviteMemberOrViewer or
            WorkspaceAction.ChangeMemberOrViewerRole or
            WorkspaceAction.RemoveMemberOrViewer => role is
                WorkspaceRoles.Owner or WorkspaceRoles.Admin,

            WorkspaceAction.InviteAdmin or
            WorkspaceAction.ChangeAdmin or
            WorkspaceAction.RemoveAdmin or
            WorkspaceAction.TransferOwnership =>
                role == WorkspaceRoles.Owner,

            WorkspaceAction.LeaveWorkspace => role is
                WorkspaceRoles.Admin or
                WorkspaceRoles.Member or
                WorkspaceRoles.Viewer,

            _ => false
        };
}
```

The explicit default arm is required so casting an undefined integer to
`WorkspaceAction` cannot grant Owner access.

- [ ] **Step 4: Compile the Application project**

Run:

```bash
dotnet build server/src/TempoDesk.Application/TempoDesk.Application.csproj
```

Expected: exit code `0`, zero compiler errors.

- [ ] **Step 5: Commit the permission model**

```bash
git add server/src/TempoDesk.Application/Workspaces/WorkspaceRoles.cs \
  server/src/TempoDesk.Application/Workspaces/WorkspaceAction.cs \
  server/src/TempoDesk.Application/Workspaces/WorkspacePermissionPolicy.cs
git commit -m "feat(auth): define workspace permission matrix"
```

### Task 2: Implement active-membership authorization

**Files:**
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceAuthorizationResult.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/IWorkspaceAuthorizationService.cs`
- Create: `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceAuthorizationService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs:104-106`

**Interfaces:**
- Consumes: `WorkspacePermissionPolicy.Allows(string, WorkspaceAction)` from Task 1 and `TempoDeskIdentityDbContext.WorkspaceMembers`.
- Produces: `IWorkspaceAuthorizationService.AuthorizeAsync(Guid userId, Guid workspaceId, WorkspaceAction action, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Add authorization result types**

Create `WorkspaceAuthorizationResult.cs`:

```csharp
namespace TempoDesk.Application.Workspaces;

public enum WorkspaceAuthorizationStatus
{
    Allowed,
    NotFound,
    Forbidden
}

public sealed record WorkspaceAuthorizationResult(
    WorkspaceAuthorizationStatus Status,
    string? Role = null)
{
    public static WorkspaceAuthorizationResult Granted(string role) =>
        new(WorkspaceAuthorizationStatus.Allowed, role);

    public static WorkspaceAuthorizationResult Missing { get; } =
        new(WorkspaceAuthorizationStatus.NotFound);

    public static WorkspaceAuthorizationResult Denied { get; } =
        new(WorkspaceAuthorizationStatus.Forbidden);
}
```

- [ ] **Step 2: Add the reusable Application contract**

Create `IWorkspaceAuthorizationService.cs`:

```csharp
namespace TempoDesk.Application.Workspaces;

public interface IWorkspaceAuthorizationService
{
    Task<WorkspaceAuthorizationResult> AuthorizeAsync(
        Guid userId,
        Guid workspaceId,
        WorkspaceAction action,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Implement the Infrastructure membership lookup**

Create `WorkspaceAuthorizationService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.Workspaces;

internal sealed class WorkspaceAuthorizationService(
    TempoDeskIdentityDbContext context) : IWorkspaceAuthorizationService
{
    public async Task<WorkspaceAuthorizationResult> AuthorizeAsync(
        Guid userId,
        Guid workspaceId,
        WorkspaceAction action,
        CancellationToken cancellationToken = default)
    {
        var role = await context.WorkspaceMembers
            .AsNoTracking()
            .Where(member => member.UserId == userId &&
                member.WorkspaceId == workspaceId &&
                member.RemovedAt == null)
            .Select(member => member.Role)
            .SingleOrDefaultAsync(cancellationToken);

        if (role is null)
            return WorkspaceAuthorizationResult.Missing;

        return WorkspacePermissionPolicy.Allows(role, action)
            ? WorkspaceAuthorizationResult.Granted(role)
            : WorkspaceAuthorizationResult.Denied;
    }
}
```

- [ ] **Step 4: Register the authorization boundary**

In `IdentityConfiguration.AddTempoDeskIdentity`, place the authorization
registration immediately before `IWorkspaceService`:

```csharp
services.AddScoped<PersonalWorkspaceProvisioner>();
services.AddScoped<IWorkspaceAuthorizationService,
    WorkspaceAuthorizationService>();
services.AddScoped<IWorkspaceService, WorkspaceService>();
```

- [ ] **Step 5: Compile Infrastructure**

Run:

```bash
dotnet build server/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj
```

Expected: exit code `0`, zero compiler errors.

- [ ] **Step 6: Commit the authorization service**

```bash
git add server/src/TempoDesk.Application/Workspaces/WorkspaceAuthorizationResult.cs \
  server/src/TempoDesk.Application/Workspaces/IWorkspaceAuthorizationService.cs \
  server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceAuthorizationService.cs \
  server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs
git commit -m "feat(auth): authorize active workspace memberships"
```

### Task 3: Enforce the policy on existing workspace operations

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceService.cs:8-132`
- Modify: `server/src/TempoDesk.Infrastructure/Workspaces/PersonalWorkspaceProvisioner.cs:1-46`
- Delete: `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceRoleValues.cs`

**Interfaces:**
- Consumes: `IWorkspaceAuthorizationService`, `WorkspacePermissionPolicy`, `WorkspaceRoles`, and the existing `IWorkspaceService` contract.
- Produces: list/read/rename behavior governed by the centralized policy with existing HTTP result mappings unchanged.

- [ ] **Step 1: Route list, read, and rename through the policy**

Replace `WorkspaceService.cs` with:

```csharp
using Microsoft.EntityFrameworkCore;
using TempoDesk.Application.Workspaces;
using TempoDesk.Infrastructure.Identity;
using TempoDesk.Infrastructure.Workspaces.Persistence;

namespace TempoDesk.Infrastructure.Workspaces;

internal sealed class WorkspaceService(
    TempoDeskIdentityDbContext context,
    IWorkspaceAuthorizationService authorization,
    TimeProvider timeProvider) : IWorkspaceService
{
    public async Task<IReadOnlyList<WorkspaceSummary>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var memberships = await context.WorkspaceMembers
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

        return memberships
            .Where(workspace => WorkspacePermissionPolicy.Allows(
                workspace.Role, WorkspaceAction.ViewWorkspace))
            .ToList();
    }

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
            Role = WorkspaceRoles.Owner,
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
        var access = await authorization.AuthorizeAsync(
            userId,
            workspaceId,
            WorkspaceAction.ViewWorkspace,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure(access);

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

        var access = await authorization.AuthorizeAsync(
            userId,
            workspaceId,
            WorkspaceAction.RenameWorkspace,
            cancellationToken);
        if (access.Status != WorkspaceAuthorizationStatus.Allowed)
            return AuthorizationFailure(access);

        var membership = await AccessibleWorkspace(userId, workspaceId)
            .Include(member => member.Workspace)
            .SingleOrDefaultAsync(cancellationToken);
        if (membership is null)
            return WorkspaceResult.Missing;

        if (membership.Workspace.Type == WorkspaceTypeValues.Personal)
            return WorkspaceResult.PersonalNameConflict;

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

    private static WorkspaceResult AuthorizationFailure(
        WorkspaceAuthorizationResult authorization) =>
        authorization.Status switch
        {
            WorkspaceAuthorizationStatus.NotFound => WorkspaceResult.Missing,
            WorkspaceAuthorizationStatus.Forbidden => WorkspaceResult.Denied,
            _ => throw new InvalidOperationException(
                "Allowed authorization is not a failure.")
        };

    private static WorkspaceSummary ToSummary(
        Workspace workspace,
        string role) => new(
            workspace.Id,
            workspace.Name,
            workspace.Type,
            role);
}
```

The second active-membership query in read and rename is intentional: if the
membership is removed after authorization but before resource access, the
operation still returns `NotFound` and exposes no workspace data.

- [ ] **Step 2: Use the Application Owner value during personal provisioning**

Add this using to `PersonalWorkspaceProvisioner.cs`:

```csharp
using TempoDesk.Application.Workspaces;
```

Replace the SQL interpolation value:

```csharp
{WorkspaceRoles.Owner},
```

- [ ] **Step 3: Remove the superseded Infrastructure role constants**

Delete:

```text
server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceRoleValues.cs
```

- [ ] **Step 4: Prove no production caller uses the old constants or inline role comparison**

Run:

```bash
rg -n "WorkspaceRoleValues|membership\.Role is|membership\.Role ==" \
  server/src -g '*.cs'
```

Expected: no matches and exit code `1` from `rg`.

- [ ] **Step 5: Build the complete solution**

Run:

```bash
dotnet build server/TempoDesk.sln
```

Expected: exit code `0`, zero warnings, zero errors.

- [ ] **Step 6: Commit endpoint enforcement**

```bash
git add server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceService.cs \
  server/src/TempoDesk.Infrastructure/Workspaces/PersonalWorkspaceProvisioner.cs \
  server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceRoleValues.cs
git commit -m "feat(auth): enforce workspace permissions"
```

### Task 4: Audit ticket scope and final verification

**Files:**
- Review: all files changed since the design commit `eaf19f8`.

**Interfaces:**
- Consumes: completed Tasks 1-3.
- Produces: fresh build evidence and a scope/security audit suitable for handoff.

- [ ] **Step 1: Inspect the complete implementation diff**

Run:

```bash
git diff --stat eaf19f8..HEAD
git diff --check eaf19f8..HEAD
git diff eaf19f8..HEAD -- server/src
```

Expected: only SCRUM-34 Application/Infrastructure files plus its tracked docs;
`git diff --check` exits `0`.

- [ ] **Step 2: Audit authorization call sites and client role inputs**

Run:

```bash
rg -n "AuthorizeAsync|WorkspacePermissionPolicy\.Allows" \
  server/src -g '*.cs'
rg -n "Role|role" server/src/TempoDesk.Api/Features/Workspaces -g '*.cs'
```

Expected: list uses the pure policy, read/rename use `AuthorizeAsync`, and the
workspace API contains only the response role field—no request role field.

- [ ] **Step 3: Run the ticket's fresh verification command**

Run:

```bash
dotnet build server/TempoDesk.sln
```

Expected: exit code `0`, zero warnings, zero errors.

- [ ] **Step 4: Confirm a clean tracked worktree**

Run:

```bash
git status --short
```

Expected: no output. If the plan checklist itself was updated during execution,
commit only that documentation change with `docs: complete SCRUM-34 plan` and
run `git status --short` again.

## Deferred Automated Coverage

SCRUM-37 must restore the minimum Sprint 2 server test infrastructure and cover
the full role/action matrix, undefined actions, unknown roles, removed
memberships, cross-workspace access, and endpoint status mappings. This is a
deliberate ticket boundary approved for SCRUM-34, not a claim that security
logic needs no tests.
