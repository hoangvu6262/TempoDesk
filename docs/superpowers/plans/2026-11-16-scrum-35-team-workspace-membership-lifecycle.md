# SCRUM-35 Team Workspace Membership Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the complete backend Team Workspace invitation and membership lifecycle with safe authorization, token handling, concurrency, and ownership transfer.

**Architecture:** Add a dedicated Application contract and one Infrastructure `WorkspaceMembershipService` that owns the lifecycle transaction boundary while reusing SCRUM-34 authorization. Expose the service through authenticated API controllers, deliver raw tokens only through a focused SMTP boundary, and restore one xUnit project backed by PostgreSQL Testcontainers so database locks and constraints are exercised for real.

**Tech Stack:** .NET 10, C# 14, ASP.NET Core, EF Core 10, Npgsql/PostgreSQL 18, ASP.NET Identity, xUnit 2.9.3, Testcontainers.PostgreSql 4.16.0.

## Global Constraints

- Backend only; do not create or modify files under `client/`.
- Invitation lifetime is exactly seven days.
- Generate 32 random bytes, encode unpadded base64url, and persist only lowercase SHA-256 hashes.
- Never return or log raw invitation tokens or token hashes.
- Derive the caller only from the authenticated `NameIdentifier` claim.
- Apply the SCRUM-34 permission action selected from persisted source and target roles.
- Keep acceptance and membership creation in one PostgreSQL transaction.
- Keep ownership transfer and both role updates in one PostgreSQL transaction.
- For every workspace-scoped mutation, lock the workspace before loading target
  rows and re-run authorization while holding that lock; this prevents a
  concurrently removed actor from committing a later mutation.
- Do not add reminders, scheduled cleanup, bulk invite, SCIM, custom roles, or frontend behavior.

---

## File Map

- Create `server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj`: the single restored backend test project.
- Create `server/tests/TempoDesk.Tests/Database/PostgreSqlFixture.cs`: starts PostgreSQL, installs roles, applies migrations, and resets test data.
- Create `server/tests/TempoDesk.Tests/Workspaces/WorkspaceMembershipPolicyTests.cs`: role/action selection tests.
- Create `server/tests/TempoDesk.Tests/Workspaces/WorkspaceInvitationTests.cs`: issuance, cancellation, listing, token, and concurrent acceptance tests.
- Create `server/tests/TempoDesk.Tests/Workspaces/WorkspaceMemberLifecycleTests.cs`: role, remove, leave, and transfer tests.
- Create `server/tests/TempoDesk.Tests/Workspaces/WorkspaceMembershipApiTests.cs`: authentication, status, and DTO contract tests.
- Create `server/src/TempoDesk.Application/Workspaces/IWorkspaceMembershipService.cs`: lifecycle use-case interface.
- Create `server/src/TempoDesk.Application/Workspaces/WorkspaceMembershipModels.cs`: member and invitation summaries.
- Create `server/src/TempoDesk.Application/Workspaces/WorkspaceMembershipResult.cs`: stable result/status contract.
- Create `server/src/TempoDesk.Application/Workspaces/WorkspaceMembershipPolicy.cs`: persisted-role-to-action selection.
- Create `server/src/TempoDesk.Application/Workspaces/IWorkspaceInvitationSender.cs`: raw-token delivery boundary.
- Create `server/src/TempoDesk.Application/Workspaces/WorkspaceInvitationEmail.cs`: invitation email message.
- Create `server/src/TempoDesk.Application/Workspaces/WorkspaceInvitationEmailTemplates.cs`: safe invitation link content.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceInvitationToken.cs`: generate, validate, and hash token material without logging it.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceMembershipService.cs`: EF Core lifecycle implementation.
- Create `server/src/TempoDesk.Infrastructure/Email/Workspaces/SmtpWorkspaceInvitationSender.cs`: SMTP delivery.
- Create `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceMembershipRequestDtos.cs`: request DTOs.
- Create `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceMembershipResponseDtos.cs`: public response DTOs without token fields.
- Create `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceMembershipController.cs`: workspace-scoped lifecycle routes.
- Create `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceInvitationAcceptanceController.cs`: authenticated token acceptance route.
- Modify `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`: register membership service and invitation sender.
- Modify `server/TempoDesk.sln`: include `TempoDesk.Tests`.
- Modify `.github/workflows/ci.yml`: execute backend tests after build.

---

### Task 1: Restore the Backend Test Boundary

**Files:**
- Create: `server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj`
- Create: `server/tests/TempoDesk.Tests/Database/PostgreSqlFixture.cs`
- Modify: `server/TempoDesk.sln`

**Interfaces:**
- Produces: `PostgreSqlFixture.CreateContext()` and `PostgreSqlFixture.ResetAsync()` for later integration tests.
- Consumes: all SQL files under `database/migrations/` in filename order.

- [ ] **Step 1: Create the test project**

Use this project definition:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.10" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.16.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/TempoDesk.Api/TempoDesk.Api.csproj" />
    <ProjectReference Include="../../src/TempoDesk.Application/TempoDesk.Application.csproj" />
    <ProjectReference Include="../../src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
    <None Include="../../../database/migrations/*.sql" Link="Database/Migrations/%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the PostgreSQL fixture**

Start `postgres:18.6-bookworm`, create `tempodesk_migrator` and `tempodesk_runtime`, grant both roles database connect, transfer the `public` schema to the migrator, grant runtime schema usage, and install the same default table/sequence privileges as `docker/postgres/init.sql`. Apply every copied migration as the migrator and expose runtime `DatabaseOptions`. Reset only workspace invitations, members, workspaces, security audit logs, identity tokens, and users with `TRUNCATE ... CASCADE`; never drop the container database between tests.

- [ ] **Step 3: Add the project to the solution and verify discovery**

Run:

```bash
dotnet sln server/TempoDesk.sln add server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --solution-folder tests
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --list-tests
```

Expected: exit 0 and no test failures; the second command may report no tests yet.

- [ ] **Step 4: Commit the test boundary**

```bash
git add server/TempoDesk.sln server/tests/TempoDesk.Tests
git commit -m "test(workspaces): restore backend integration boundary"
```

---

### Task 2: Define Membership Contracts and Permission Selection

**Files:**
- Create: `server/src/TempoDesk.Application/Workspaces/IWorkspaceMembershipService.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceMembershipModels.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceMembershipResult.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceMembershipPolicy.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/IWorkspaceInvitationSender.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceInvitationEmail.cs`
- Create: `server/src/TempoDesk.Application/Workspaces/WorkspaceInvitationEmailTemplates.cs`
- Test: `server/tests/TempoDesk.Tests/Workspaces/WorkspaceMembershipPolicyTests.cs`

**Interfaces:**
- Produces: `IWorkspaceMembershipService`, `WorkspaceMembershipResult`, `WorkspaceMemberSummary`, `WorkspaceInvitationSummary`, `IWorkspaceInvitationSender`.
- Consumes: `WorkspaceAction`, `WorkspaceRoles`, and `EmailContent`.

- [ ] **Step 1: Write failing permission-selection tests**

Cover these exact mappings:

```csharp
Assert.Equal(WorkspaceAction.InviteAdmin,
    WorkspaceMembershipPolicy.InvitationAction(WorkspaceRoles.Admin));
Assert.Equal(WorkspaceAction.InviteMemberOrViewer,
    WorkspaceMembershipPolicy.InvitationAction(WorkspaceRoles.Member));
Assert.Equal(WorkspaceAction.ChangeAdmin,
    WorkspaceMembershipPolicy.RoleChangeAction(WorkspaceRoles.Admin, WorkspaceRoles.Member));
Assert.Equal(WorkspaceAction.InviteAdmin,
    WorkspaceMembershipPolicy.RoleChangeAction(WorkspaceRoles.Member, WorkspaceRoles.Admin));
Assert.Equal(WorkspaceAction.ChangeMemberOrViewerRole,
    WorkspaceMembershipPolicy.RoleChangeAction(WorkspaceRoles.Member, WorkspaceRoles.Viewer));
Assert.Null(WorkspaceMembershipPolicy.RoleChangeAction(WorkspaceRoles.Owner, WorkspaceRoles.Admin));
Assert.Equal(WorkspaceAction.RemoveAdmin,
    WorkspaceMembershipPolicy.RemovalAction(WorkspaceRoles.Admin));
Assert.Null(WorkspaceMembershipPolicy.RemovalAction(WorkspaceRoles.Owner));
```

- [ ] **Step 2: Run RED**

Run: `dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceMembershipPolicyTests`

Expected: compilation fails because `WorkspaceMembershipPolicy` does not exist.

- [ ] **Step 3: Add the minimal policy and models**

Implement nullable action selectors that fail closed for unknown roles. Define:

```csharp
public sealed record WorkspaceMemberSummary(
    Guid Id, string Email, string Role, DateTimeOffset JoinedAt);

public sealed record WorkspaceInvitationSummary(
    Guid Id, string Email, string Role,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public enum WorkspaceMembershipResultStatus
{
    Success,
    InvalidRequest,
    InvalidInvitation,
    NotFound,
    Forbidden,
    Conflict
}
```

`WorkspaceMembershipResult` carries at most one member, one invitation, one member list, one invitation list, and optional validation field/message. Its factories are `Succeeded()`, `WithMember`, `WithInvitation`, `WithMembers`, `WithInvitations`, `Invalid`, `InvalidToken`, `Missing`, `Denied`, and `Conflicted`.

Define the service contract exactly as follows:

```csharp
public interface IWorkspaceMembershipService
{
    Task<WorkspaceMembershipResult> ListMembersAsync(Guid actorUserId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceMembershipResult> ListInvitationsAsync(Guid actorUserId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceMembershipResult> InviteAsync(Guid actorUserId, Guid workspaceId, string? email, string? role, CancellationToken cancellationToken = default);
    Task<WorkspaceMembershipResult> CancelInvitationAsync(Guid actorUserId, Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default);
    Task<WorkspaceMembershipResult> AcceptAsync(Guid userId, string? token, CancellationToken cancellationToken = default);
    Task<WorkspaceMembershipResult> ChangeRoleAsync(Guid actorUserId, Guid workspaceId, Guid memberId, string? role, CancellationToken cancellationToken = default);
    Task<WorkspaceMembershipResult> RemoveMemberAsync(Guid actorUserId, Guid workspaceId, Guid memberId, CancellationToken cancellationToken = default);
    Task<WorkspaceMembershipResult> LeaveAsync(Guid actorUserId, Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceMembershipResult> TransferOwnershipAsync(Guid actorUserId, Guid workspaceId, Guid targetMemberId, CancellationToken cancellationToken = default);
}
```

Define `WorkspaceInvitationEmail(string Email, string WorkspaceName, string Token, DateTimeOffset ExpiresAt)` and `IWorkspaceInvitationSender.SendAsync` returning `Task<bool>`.

- [ ] **Step 4: Add and test the email template**

`WorkspaceInvitationEmailTemplates.Invitation` must build `/accept-invitation?token=<escaped>` under `FrontendBaseUrl`, HTML-encode the link, name the workspace, and never include a token hash. Add a test asserting both text and HTML contain the escaped raw token link and the workspace name.

- [ ] **Step 5: Run GREEN and commit**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceMembershipPolicyTests
git add server/src/TempoDesk.Application/Workspaces server/tests/TempoDesk.Tests/Workspaces/WorkspaceMembershipPolicyTests.cs
git commit -m "feat(workspaces): define membership lifecycle contracts"
```

Expected: all selected tests pass.

---

### Task 3: Issue, List, and Cancel Invitations

**Files:**
- Create: `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceInvitationToken.cs`
- Create: `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceMembershipService.cs`
- Test: `server/tests/TempoDesk.Tests/Workspaces/WorkspaceInvitationTests.cs`

**Interfaces:**
- Consumes: `TempoDeskIdentityDbContext`, `IWorkspaceAuthorizationService`, `ILookupNormalizer`, `IWorkspaceInvitationSender`, and `TimeProvider`.
- Produces: list, invite, and cancel implementations on `IWorkspaceMembershipService`.

- [ ] **Step 1: Write failing issuance tests**

Seed Owner, Admin, Member, Viewer, one Team Workspace, and one Personal Workspace. Assert:

- Owner can issue Admin, Member, and Viewer invitations.
- Admin can issue Member and Viewer but receives Forbidden for Admin.
- Member and Viewer receive Forbidden.
- Personal Workspace issuance returns Conflict.
- email is trimmed and Identity-normalized before persistence;
- persisted `token_hash` is 64 lowercase hex characters and captured sender token is absent from the entity;
- returned summary has no token property;
- duplicate active invitation and active membership return Conflict;
- an expired invitation is cancelled and replaced by a new seven-day invitation.

- [ ] **Step 2: Run RED**

Run: `dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceInvitationTests.Issue`

Expected: compilation fails because `WorkspaceMembershipService` does not exist.

- [ ] **Step 3: Implement secure token handling and issuance**

`WorkspaceInvitationToken.Create()` uses `RandomNumberGenerator.GetBytes(32)` and `WebEncoders.Base64UrlEncode`. `TryHash` accepts exactly the canonical 43-character base64url form, decodes to exactly 32 bytes, and returns lowercase SHA-256; every other input returns false without throwing.

`InviteAsync` validates `MailAddress.TryCreate`, normalizes through `ILookupNormalizer.NormalizeEmail`, and selects `InvitationAction`. It then begins a transaction, locks the workspace, authorizes against the current active actor membership while holding the lock, rejects non-Team type, cancels expired matching invitations, rejects an active membership whose user's normalized email matches, inserts with `expires_at = now + 7 days`, commits, then calls the sender. Catch only PostgreSQL unique violations for the two invitation uniqueness constraints and map them to Conflict.

- [ ] **Step 4: Add list and cancellation tests, then run RED**

Assert list methods require `ViewMembers`, members contain active rows only, invitations contain pending unexpired rows only, DTO models contain only approved fields, cancellation derives authority from the stored invitation role, and accepted/cancelled IDs return NotFound.

Run: `dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter "WorkspaceInvitationTests.List|WorkspaceInvitationTests.Cancel"`

Expected: tests fail because list and cancel methods are not implemented.

- [ ] **Step 5: Implement list and cancellation, run GREEN, and commit**

Project summaries directly in EF queries so persistence entities and unrelated user fields are never materialized into public models. Cancel with a locked invitation row, persisted-role action selection, Team Workspace check, and a single `cancelled_at` update.

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceInvitationTests
git add server/src/TempoDesk.Infrastructure/Workspaces server/tests/TempoDesk.Tests/Workspaces/WorkspaceInvitationTests.cs
git commit -m "feat(workspaces): issue and manage invitations"
```

Expected: all invitation issuance, list, and cancellation tests pass.

---

### Task 4: Accept Invitations Atomically

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceMembershipService.cs`
- Test: `server/tests/TempoDesk.Tests/Workspaces/WorkspaceInvitationTests.cs`

**Interfaces:**
- Implements: `AcceptAsync(Guid userId, string? token, CancellationToken cancellationToken)`.
- Produces: exactly one active `WorkspaceMember` and one consumed invitation on success.

- [ ] **Step 1: Write failing lifecycle and concurrency tests**

Test missing, blank, malformed, unknown, expired, cancelled, reused, wrong-email, and Personal Workspace invitations. Every token failure must return `InvalidInvitation`. Test a removed historical membership followed by acceptance creates a new row. Run two service instances concurrently with the same token and assert exactly one Success, one InvalidInvitation, one active membership, and one non-null `accepted_at`.

- [ ] **Step 2: Run RED**

Run: `dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceInvitationTests.Accept`

Expected: tests fail because acceptance is not implemented.

- [ ] **Step 3: Implement locked transactional acceptance**

Reject malformed input before opening a transaction. Hash the token, begin a transaction, and query the invitation with:

```csharp
context.WorkspaceInvitations.FromSqlInterpolated($"""
    SELECT * FROM public.workspace_invitations
    WHERE token_hash = {tokenHash}
    FOR UPDATE
    """)
```

After the lock, re-check lifecycle and expiry, load only the accepting user's normalized email, compare with `StringComparison.Ordinal`, reject Personal Workspace, reject an existing active membership, add a new membership, set `AcceptedAt`, save once, and commit. Mark an expired candidate cancelled before returning InvalidInvitation. Catch the active-membership unique violation and return InvalidInvitation after rollback.

- [ ] **Step 4: Run GREEN and commit**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceInvitationTests.Accept
git add server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceMembershipService.cs server/tests/TempoDesk.Tests/Workspaces/WorkspaceInvitationTests.cs
git commit -m "feat(workspaces): accept invitations atomically"
```

Expected: all acceptance tests, including the concurrent test, pass.

---

### Task 5: Manage Roles, Removal, Leaving, and Ownership

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceMembershipService.cs`
- Test: `server/tests/TempoDesk.Tests/Workspaces/WorkspaceMemberLifecycleTests.cs`

**Interfaces:**
- Implements: `ChangeRoleAsync`, `RemoveMemberAsync`, `LeaveAsync`, and `TransferOwnershipAsync`.
- Consumes: action selectors from `WorkspaceMembershipPolicy`.

- [ ] **Step 1: Write failing role and removal tests**

Cover Owner and Admin permissions for Member/Viewer transitions, Owner-only Admin promotion/demotion, Admin denial against Admin and Owner, rejection of `owner` as a requested role, immediate SCRUM-34 access loss after `removed_at`, Owner removal denial, and Personal Workspace conflict.

- [ ] **Step 2: Run RED, implement minimal role/remove/leave behavior, and run GREEN**

Run RED:

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter "WorkspaceMemberLifecycleTests.Role|WorkspaceMemberLifecycleTests.Remove|WorkspaceMemberLifecycleTests.Leave"
```

Lock the workspace before target membership rows so all owner-sensitive mutations use one lock order. Choose authorization only after loading the persisted target role, and perform authorization while the workspace lock is held. A same-role request is an idempotent Success after authorization. Leave targets the actor's own active membership and uses `LeaveWorkspace`; Owner receives Forbidden, Personal Workspace receives Conflict, and other roles receive `removed_at = now`.

Run the same command again. Expected: selected tests pass.

- [ ] **Step 3: Write failing ownership transfer tests**

Assert Personal Workspace Conflict, non-Owner Forbidden, missing or removed target NotFound, current Owner cannot target itself, target Member/Viewer/Admin all become Owner, former Owner becomes Admin, and both `owner_user_id` and active Owner membership agree after commit. Race two transfers and assert only one final valid Owner.

- [ ] **Step 4: Run RED and implement atomic transfer**

Run: `dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceMemberLifecycleTests.Transfer`

Expected: transfer tests fail.

Begin a transaction, lock the workspace with `FOR UPDATE`, authorize `TransferOwnership`, load current and target active memberships, reject Personal/self/missing targets, set former Owner to Admin, target to Owner, `OwnerUserId` to target user ID, and `UpdatedAt` to now. Save and commit once so the deferred owner constraint observes only the final state.

- [ ] **Step 5: Run GREEN and commit**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceMemberLifecycleTests
git add server/src/TempoDesk.Infrastructure/Workspaces/WorkspaceMembershipService.cs server/tests/TempoDesk.Tests/Workspaces/WorkspaceMemberLifecycleTests.cs
git commit -m "feat(workspaces): manage membership and ownership"
```

Expected: all lifecycle tests pass.

---

### Task 6: Expose Safe API and SMTP Boundaries

**Files:**
- Create: `server/src/TempoDesk.Infrastructure/Email/Workspaces/SmtpWorkspaceInvitationSender.cs`
- Create: `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceMembershipRequestDtos.cs`
- Create: `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceMembershipResponseDtos.cs`
- Create: `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceMembershipController.cs`
- Create: `server/src/TempoDesk.Api/Features/Workspaces/WorkspaceInvitationAcceptanceController.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Test: `server/tests/TempoDesk.Tests/Workspaces/WorkspaceMembershipApiTests.cs`

**Interfaces:**
- Consumes: `IWorkspaceMembershipService` and `IWorkspaceInvitationSender`.
- Produces: all nine SCRUM-35 HTTP routes and safe response DTOs.

- [ ] **Step 1: Write failing controller/serialization tests**

Use a fake membership service and authenticated test principal. Assert unauthenticated requests return 401; `NameIdentifier` is the actor passed to the service; status mapping is Success to 200/201/204, InvalidRequest and InvalidInvitation to 400, NotFound to 404, Forbidden to 403, and Conflict to 409. Serialize both DTOs and assert property names are exactly:

```text
member: id,email,role,joinedAt
invitation: id,email,role,createdAt,expiresAt
```

Assert case-insensitively that neither serialized body contains `token`, `hash`, `password`, `securityStamp`, or `concurrencyStamp`.

- [ ] **Step 2: Run RED**

Run: `dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceMembershipApiTests`

Expected: compilation fails because controllers and DTOs do not exist.

- [ ] **Step 3: Implement DTOs, controllers, and result mapping**

Request records are:

```csharp
public sealed record InviteWorkspaceMemberRequestDto(string? Email, string? Role);
public sealed record AcceptWorkspaceInvitationRequestDto(string? Token);
public sealed record ChangeWorkspaceMemberRoleRequestDto(string? Role);
public sealed record TransferWorkspaceOwnershipRequestDto(Guid MemberId);
```

Keep `[Authorize]` on both controllers. Use one private `UserId()` per controller and one exhaustive result mapper. Validation failures add only the service-provided safe field/message to `ModelState`; invalid invitation uses the fixed message `The invitation is invalid or no longer active.`

- [ ] **Step 4: Implement SMTP sender and DI registration**

Follow existing authentication senders: when Email is disabled return false; otherwise render `WorkspaceInvitationEmailTemplates.Invitation`, send text and HTML alternatives, and never log or catch with token-bearing data. Register `IWorkspaceMembershipService` scoped to `WorkspaceMembershipService` and `IWorkspaceInvitationSender` scoped to `SmtpWorkspaceInvitationSender`.

- [ ] **Step 5: Run GREEN and commit**

```bash
dotnet test server/tests/TempoDesk.Tests/TempoDesk.Tests.csproj --filter WorkspaceMembershipApiTests
git add server/src/TempoDesk.Api/Features/Workspaces server/src/TempoDesk.Infrastructure/Email/Workspaces server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs server/tests/TempoDesk.Tests/Workspaces/WorkspaceMembershipApiTests.cs
git commit -m "feat(api): expose workspace membership lifecycle"
```

Expected: all API and DTO tests pass.

---

### Task 7: Wire CI and Verify the Complete Acceptance Matrix

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify only if checks expose defects: SCRUM-35 files listed above.

**Interfaces:**
- Produces: repeatable build/test evidence and CI execution for the restored backend suite.

- [ ] **Step 1: Add backend test execution to CI**

After the Release build step add:

```yaml
      - run: dotnet test server/TempoDesk.sln --configuration Release --no-build
```

- [ ] **Step 2: Run formatting and token-surface scans**

```bash
dotnet format server/TempoDesk.sln --verify-no-changes
rg -n "TokenHash|token_hash|\.Token\b" server/src/TempoDesk.Api server/src/TempoDesk.Application/Workspaces
rg -n "Log(Trace|Debug|Information|Warning|Error|Critical).*token|Console\.(Write|Error).*token" server/src
git diff --check
```

Expected: formatting and diff checks exit 0; token-property hits are limited to internal delivery message/template code and no logging calls contain token material.

- [ ] **Step 3: Run the complete fresh verification suite**

```bash
dotnet test server/TempoDesk.sln --configuration Release
dotnet build server/TempoDesk.sln --configuration Release
```

Expected: both commands exit 0 with zero failed tests and zero build warnings/errors.

- [ ] **Step 4: Confirm scope and acceptance criteria**

```bash
git diff development...HEAD --name-only
git status --short
```

Expected: no path under `client/`; working tree clean after the final commit. Check each SCRUM-35 acceptance criterion against a named passing test before claiming completion.

- [ ] **Step 5: Commit CI wiring or verification fixes**

```bash
git add .github/workflows/ci.yml server/src server/tests server/TempoDesk.sln
git commit -m "test(workspaces): verify SCRUM-35 lifecycle"
```

Do not create an empty commit when no verification fix or CI change remains.
