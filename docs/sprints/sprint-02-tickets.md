# TempoDesk Sprint 2 Tickets — Workspace and Authorization

**Sprint:** 2  
**Duration:** 2 weeks  
**Sprint goal:** Let one account use a private Personal Workspace and multiple Team Workspaces while every API operation enforces an explicit workspace permission model.

> **Jira key note:** `SCRUM-32` through `SCRUM-37` are proposed keys based on the current repository sequence. Jira is the source of truth; update this document if Jira assigns different keys.

## Sprint outcome

At the end of Sprint 2, every account has exactly one Personal Workspace, users
can create and switch between Team Workspaces, and authorized users can invite
and manage members. Workspace data is isolated at the API and database
boundaries, not only hidden by the frontend.

## Delivery principles

- Derive the acting user only from the authenticated principal.
- Scope every workspace query and mutation by both workspace and membership.
- Keep the permission matrix in application code for this sprint; do not add a
  configurable `workspace_role_permissions` table without a concrete product
  requirement.
- Store invitation-token hashes only. Never store, log, or return a reusable raw
  token after creation.
- Use the existing SQL-first migration workflow and generic Problem Details
  error contract.
- Use UTC timestamps and database constraints for uniqueness and lifecycle
  invariants.
- Treat frontend permission checks as usability only; the backend remains the
  authority.

## Ticket summary

| ID       | Type  | Title                                                    | Priority | Points | Depends on                   |
| -------- | ----- | -------------------------------------------------------- | -------- | -----: | ---------------------------- |
| SCRUM-32 | Story | Create the workspace and membership data model           | Highest  |      5 | Sprint 1                     |
| SCRUM-33 | Story | Provision personal workspaces and team workspace APIs    | Highest  |      5 | SCRUM-32                     |
| SCRUM-34 | Story | Enforce workspace roles and authorization policies       | Highest  |      8 | SCRUM-32, SCRUM-33           |
| SCRUM-35 | Story | Implement invitations and member lifecycle management    | Highest  |      8 | SCRUM-32, SCRUM-34           |
| SCRUM-36 | Story | Build the workspace switcher and management UI           | High     |      8 | SCRUM-33, SCRUM-34, SCRUM-35 |
| SCRUM-37 | Task  | Add workspace auditing and automated acceptance coverage | Highest  |      8 | SCRUM-33–SCRUM-36            |

**Planned total:** 42 story points. The six-ticket structure deliberately groups
related use cases; the team should re-estimate before sprint commitment.

---

## SCRUM-32 — Create the workspace and membership data model

**Type:** Story  
**Priority:** Highest  
**Estimate:** 5 points  
**Owner:** Backend/Database  
**Dependencies:** Sprint 1 identity schema

### Description

Create the PostgreSQL schema and application persistence model required for
Personal Workspaces, Team Workspaces, memberships, and invitations. Encode the
critical ownership and uniqueness rules in the database so concurrent requests
cannot create invalid workspace state.

### Implementation scope

- Add a SQL migration for `workspaces`, `workspace_members`, and
  `workspace_invitations`.
- Define workspace type values `personal` and `team` with a database check
  constraint.
- Define membership roles `owner`, `admin`, `member`, and `viewer` with a
  database check constraint.
- Store the workspace owner explicitly and require that owner to have an active
  owner membership.
- Enforce at most one Personal Workspace per user with a partial unique index.
- Enforce at most one active membership per `(workspace_id, user_id)` with a
  partial unique index or an equivalent non-destructive membership model.
- Store invitation email in normalized form and store only a unique token hash.
- Track invitation creation, expiry, acceptance, and cancellation timestamps.
- Prevent more than one active invitation for the same normalized email in the
  same workspace.
- Add foreign keys and indexes for owner, member, invitation, expiry, and active
  membership lookup paths.
- Map the new tables into Infrastructure without putting business rules in
  generated persistence entities.
- Do not create `workspace_role_permissions`; Sprint 2 uses a fixed permission
  matrix in application code.

### Acceptance criteria

1. A workspace type other than `personal` or `team` is rejected by PostgreSQL.
2. A membership role outside Owner, Admin, Member, or Viewer is rejected.
3. A user cannot own two Personal Workspaces.
4. A user cannot have two active memberships in one workspace.
5. Invitation token hashes are unique and raw tokens are never persisted.
6. An active duplicate invitation for the same workspace and email is rejected.
7. Deleting a user or workspace cannot leave orphaned memberships or
   invitations.
8. The migration is repeatable through the existing database migrator and EF
   mappings match the SQL schema.

### Verification

```bash
dotnet run --project server/tools/TempoDesk.DatabaseMigrator
dotnet build server/TempoDesk.sln
```

### Out of scope

- Custom roles or user-configurable permissions.
- Projects, tasks, boards, or other workspace-owned product data.
- Workspace deletion and retention policy.

---

## SCRUM-33 — Provision personal workspaces and team workspace APIs

**Type:** Story  
**Priority:** Highest  
**Estimate:** 5 points  
**Owner:** Backend  
**Dependencies:** SCRUM-32

### Description

Guarantee that every account has one Personal Workspace and expose the basic API
operations needed to list workspaces, create a Team Workspace, and update its
profile. A newly created Team Workspace must immediately contain the creator as
its Owner.

### Implementation scope

- Create a Personal Workspace and owner membership during successful account
  registration.
- Make Personal Workspace provisioning idempotent and protected by the database
  uniqueness constraint.
- Backfill one Personal Workspace for users created before Sprint 2.
- Add application use cases to list the caller's active workspaces, create a
  Team Workspace, read one accessible workspace, and rename a Team Workspace.
- Create the Team Workspace and its Owner membership in one transaction.
- Return the caller's role with each workspace summary.
- Keep Personal Workspace naming deterministic and safe when the account email
  or profile is unavailable.
- Reject renaming a Personal Workspace if product rules keep its name managed by
  the system.
- Return only workspaces where the authenticated user has an active membership.
- Use Problem Details for validation, not-found, forbidden, and conflict
  responses.

### Proposed API contract

- `GET /api/workspaces` — list the caller's active Personal and Team Workspaces.
- `POST /api/workspaces` — create a Team Workspace.
- `GET /api/workspaces/{workspaceId}` — read an accessible workspace.
- `PATCH /api/workspaces/{workspaceId}` — rename a Team Workspace when allowed.

No endpoint accepts a user ID identifying the caller.

### Acceptance criteria

1. Every newly registered account receives exactly one Personal Workspace.
2. Existing Sprint 1 accounts receive exactly one Personal Workspace after the
   backfill runs.
3. Repeating provisioning cannot create a second Personal Workspace.
4. Creating a Team Workspace also creates an active Owner membership for the
   caller in the same transaction.
5. `GET /api/workspaces` returns only active memberships belonging to the
   authenticated user.
6. A user cannot read or rename an unrelated workspace by guessing its ID.
7. Validation and conflict failures use the repository's Problem Details
   contract and expose no membership data from another workspace.

### Verification

```bash
dotnet build server/TempoDesk.sln
```

### Out of scope

- Invitations and member administration.
- Configurable workspace branding, avatars, domains, or billing.
- Workspace deletion.

---

## SCRUM-34 — Enforce workspace roles and authorization policies

**Type:** Story  
**Priority:** Highest  
**Estimate:** 8 points  
**Owner:** Backend/Security  
**Dependencies:** SCRUM-32, SCRUM-33

### Description

Introduce resource-based authorization for workspace actions. Controllers and
future workspace-owned features must ask one shared policy service instead of
duplicating role checks or trusting role values supplied by the client.

### Permission matrix

| Action                     | Owner | Admin | Member | Viewer |
| -------------------------- | :---: | :---: | :----: | :----: |
| View workspace             |  Yes  |  Yes  |  Yes   |  Yes   |
| Rename Team Workspace      |  Yes  |  Yes  |   No   |   No   |
| View members               |  Yes  |  Yes  |  Yes   |  Yes   |
| Invite Member or Viewer    |  Yes  |  Yes  |   No   |   No   |
| Invite or promote Admin    |  Yes  |  No   |   No   |   No   |
| Change Member/Viewer roles |  Yes  |  Yes  |   No   |   No   |
| Remove Member or Viewer    |  Yes  |  Yes  |   No   |   No   |
| Change or remove Admin     |  Yes  |  No   |   No   |   No   |
| Transfer ownership         |  Yes  |  No   |   No   |   No   |
| Leave workspace            | Yes*  |  Yes  |  Yes   |  Yes   |

`*` The Owner may leave only after ownership has been transferred to another
active member. Personal Workspace ownership cannot be transferred.

### Implementation scope

- Define stable workspace actions in the Application layer.
- Add one authorization service that loads the caller's active membership and
  evaluates the fixed matrix.
- Add ASP.NET Core resource-based authorization handlers or an equivalent
  centralized boundary.
- Require authorization on every workspace read and mutation endpoint.
- Ensure inactive or removed memberships grant no permission.
- Prevent Admin users from managing Owners or other Admins.
- Prevent all role escalation beyond the actor's authority.
- Use one consistent response for missing and inaccessible workspace resources
  where revealing existence would leak data.
- Expose the caller's effective role or allowed actions to the frontend without
  making that frontend data authoritative.
- Keep authorization logic reusable for Sprint 3 project/task resources.

### Acceptance criteria

1. Every workspace endpoint evaluates the authenticated user's active
   membership and requested action.
2. Owner, Admin, Member, and Viewer behavior matches the documented matrix.
3. A removed member immediately loses workspace access.
4. An Admin cannot promote a user to Admin, alter another Admin, or affect the
   Owner.
5. Request bodies, query strings, and headers cannot override the caller's role.
6. Requests against another workspace do not expose its name, members, or
   invitation state.
7. Future workspace-owned handlers can reuse the same authorization service
   without copying role conditionals.

### Verification

```bash
dotnet build server/TempoDesk.sln
```

### Out of scope

- Project-level roles.
- Custom roles or per-user permission overrides.
- Organization-wide administrators.

---

## SCRUM-35 — Implement invitations and member lifecycle management

**Type:** Story  
**Priority:** Highest  
**Estimate:** 8 points  
**Owner:** Backend  
**Dependencies:** SCRUM-32, SCRUM-34

### Description

Implement the complete Team Workspace membership lifecycle: invite a person by
email, accept or cancel an invitation, inspect members and pending invitations,
change roles, remove members, leave a workspace, and transfer ownership safely.

### Implementation scope

- Add use cases and endpoints for InviteMember, AcceptInvitation,
  CancelInvitation, ListMembers, ListInvitations, ChangeRole, RemoveMember,
  LeaveWorkspace, and TransferOwnership.
- Generate cryptographically secure invitation tokens and persist only their
  hashes.
- Send the raw invitation token only through the invitation link/email boundary.
- Normalize invitation email with the same rules used by account identity.
- Require the accepting authenticated account's normalized email to match the
  invitation email.
- Enforce invitation expiry, cancellation, single use, and active-invitation
  uniqueness.
- Create membership and mark the invitation accepted in one transaction.
- Make acceptance safe under concurrent or repeated requests.
- Apply the SCRUM-34 permission matrix to invitation and member operations.
- Prevent removing the Owner or leaving a Team Workspace as Owner until
  ownership is transferred.
- Transfer the workspace owner and both affected roles atomically.
- Prevent ownership transfer for Personal Workspaces.
- Return member and invitation DTOs without token hashes, raw tokens, or
  unrelated account data.

### Proposed API contract

- `GET /api/workspaces/{workspaceId}/members`
- `GET /api/workspaces/{workspaceId}/invitations`
- `POST /api/workspaces/{workspaceId}/invitations`
- `DELETE /api/workspaces/{workspaceId}/invitations/{invitationId}`
- `POST /api/workspace-invitations/accept`
- `PATCH /api/workspaces/{workspaceId}/members/{memberId}`
- `DELETE /api/workspaces/{workspaceId}/members/{memberId}`
- `POST /api/workspaces/{workspaceId}/leave`
- `POST /api/workspaces/{workspaceId}/transfer-ownership`

### Acceptance criteria

1. Owner and Admin can invite Member or Viewer roles; only Owner can invite or
   promote an Admin.
2. Invitation acceptance fails when the token is missing, malformed, expired,
   cancelled, already used, or belongs to another email.
3. Raw invitation tokens and hashes never appear in API responses or logs.
4. Successful acceptance creates exactly one active membership and consumes the
   invitation atomically.
5. Admin cannot alter Owner or Admin memberships.
6. Removing a member revokes access immediately without deleting historical
   membership identity.
7. Owner cannot leave or be removed before transferring ownership.
8. Ownership transfer updates the workspace owner and roles in one transaction.
9. Personal Workspace membership cannot be invited, removed, left, or
   transferred through Team Workspace endpoints.

### Verification

```bash
dotnet build server/TempoDesk.sln
```

### Out of scope

- Public invitation links that can be accepted without authentication.
- Invitation reminders or scheduled cleanup.
- Bulk invite, CSV import, SCIM, or domain-based auto-join.

---

## SCRUM-36 — Build the workspace switcher and management UI

**Type:** Story  
**Priority:** High  
**Estimate:** 8 points  
**Owner:** Frontend  
**Dependencies:** SCRUM-33, SCRUM-34, SCRUM-35

### Description

Build the authenticated workspace experience: an application shell with a
workspace switcher, Team Workspace creation, member and invitation management,
role changes, ownership transfer, and leave/remove actions. The UI reflects
effective permissions while treating server responses as authoritative.

### Implementation scope

- Add an authenticated application shell with workspace navigation and account
  navigation.
- Load the caller's workspace list after authentication and distinguish the
  Personal Workspace from Team Workspaces.
- Add a keyboard-accessible workspace switcher with loading, empty, and error
  states.
- Use a workspace ID in the protected route so refresh and deep links preserve
  the selected workspace.
- Add a Team Workspace creation page and navigate to the new workspace after
  success.
- Add member and pending-invitation views.
- Add forms/actions to invite, cancel an invitation, change role, remove a
  member, leave, and transfer ownership.
- Hide or disable unavailable actions based on effective permissions returned
  by the backend, while still handling HTTP 403 safely.
- Add explicit confirmation for removal, leaving, role escalation, and ownership
  transfer.
- Announce success and failure through accessible status or alert regions.
- Keep the existing device-session page reachable from account navigation,
  using `/app/account/sessions` when the workspace shell takes over `/app`.
- Never persist authorization decisions or sensitive invitation tokens in
  browser storage.

### Proposed routes

- `/app` — choose the Personal Workspace or the last valid in-memory workspace.
- `/app/workspaces/new` — create a Team Workspace.
- `/app/workspaces/{workspaceId}` — workspace landing page.
- `/app/workspaces/{workspaceId}/members` — members and invitations.
- `/app/account/sessions` — existing device-session management.

### Acceptance criteria

1. The switcher lists one Personal Workspace and every accessible Team
   Workspace without showing inaccessible workspaces.
2. Selecting a workspace updates the protected route and visible workspace
   context.
3. A user can create a Team Workspace and becomes its visible Owner.
4. Authorized users can invite, cancel, change roles, remove members, leave, and
   transfer ownership through keyboard-accessible controls.
5. Member and Viewer users do not see destructive administration actions they
   cannot perform.
6. HTTP 401 clears local authentication through the existing auth boundary;
   HTTP 403 shows a permission message without exposing hidden data.
7. Loading, empty, success, validation, conflict, and network-error states are
   visible and announced.
8. The existing session-management feature remains accessible after the new app
   shell is introduced.

### Verification

```bash
npm --prefix client/web run build
npm --prefix client/web run lint
npm --prefix client/web run format:check
```

### Out of scope

- Workspace dashboard widgets.
- Workspace avatar upload, custom branding, billing, or usage limits.
- Persisting permission decisions in local or session storage.

---

## SCRUM-37 — Add workspace auditing and automated acceptance coverage

**Type:** Task  
**Priority:** Highest  
**Estimate:** 8 points  
**Owner:** Backend/Frontend/QA  
**Dependencies:** SCRUM-33, SCRUM-34, SCRUM-35, SCRUM-36

### Description

Add security audit events and automated coverage proving workspace isolation,
the complete permission matrix, invitation safety, and the primary end-to-end
membership flow. Reintroduce only the test infrastructure required for these
behaviors; do not restore obsolete Sprint 0/1 tests wholesale.

### Implementation scope

- Record audit events for Team Workspace creation, invitations, invitation
  acceptance/cancellation, role changes, member removal/leave, and ownership
  transfer.
- Include actor user ID, target user ID when applicable, workspace ID, event
  type, outcome, and UTC timestamp.
- Exclude raw invitation tokens, token hashes, cookies, authorization headers,
  and unnecessary personal data from audit records.
- Add the minimum server API/integration test projects needed for Sprint 2.
- Test workspace provisioning and database uniqueness constraints.
- Test every SCRUM-34 role/action matrix entry.
- Test that a user cannot list, read, invite into, or mutate another workspace.
- Test invitations that are expired, cancelled, reused, concurrent, or accepted
  by the wrong email.
- Test removal, leave, and ownership-transfer invariants.
- Add focused frontend coverage for permission-based action visibility and
  mutation feedback where the restored frontend test stack is justified.
- Add one critical E2E flow: create team → invite user → accept → change role →
  verify new permissions.
- Run the new test suites in CI and preserve current build, lint, format, and
  dependency-audit gates.

### Acceptance criteria

1. Every security-sensitive workspace mutation creates a safe audit event for
   both successful and rejected attempts where appropriate.
2. Audit records contain no reusable secret or invitation token material.
3. Automated API tests cover all Owner, Admin, Member, and Viewer permission
   matrix entries.
4. Cross-workspace reads and writes are rejected and covered by tests.
5. Invitation expiry, cancellation, reuse, email mismatch, and concurrency are
   covered by automated tests.
6. The critical Team Workspace E2E flow passes from the public UI/API boundary.
7. CI runs the Sprint 2 tests and fails on regression.
8. Server build and frontend build, lint, and format checks remain green.

### Verification

```bash
dotnet test server/TempoDesk.sln
npm --prefix client/web run test
npm --prefix client/web run build
npm --prefix client/web run lint
npm --prefix client/web run format:check
```

### Out of scope

- Load, soak, or penetration testing.
- Custom audit-log search UI.
- Project/task authorization, which belongs to Sprint 3.

---

## Dependency flow

```text
SCRUM-32 Data model
    └── SCRUM-33 Personal + Team Workspace APIs
        └── SCRUM-34 Authorization policy
            └── SCRUM-35 Invitations + member lifecycle
                └── SCRUM-36 Workspace UI
                    └── SCRUM-37 Audit + acceptance coverage
```

SCRUM-33 and the initial authorization contract in SCRUM-34 may overlap after
the SCRUM-32 schema is stable. SCRUM-36 can begin with API contracts and mocked
fixtures while SCRUM-35 is being completed, but final integration and SCRUM-37
must use the real authorization boundary.

## Sprint acceptance criteria

1. Every account has exactly one Personal Workspace.
2. A user can create and switch between multiple Team Workspaces.
3. Personal Workspace data never appears in a Team Workspace context.
4. Owner, Admin, Member, and Viewer behavior matches the tested permission
   matrix.
5. Invitation tokens are expiring, single-use, email-bound, and stored only as
   hashes.
6. Owner transfer, member removal, and leaving a workspace preserve a valid
   ownership state.
7. Backend authorization protects every operation even when frontend controls
   are bypassed.
8. Critical workspace and membership changes are audited without secret data.
