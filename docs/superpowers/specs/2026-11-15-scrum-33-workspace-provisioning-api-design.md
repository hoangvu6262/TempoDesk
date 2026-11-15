# SCRUM-33 Workspace Provisioning and API Design

## Goal

Guarantee that every account has exactly one Personal Workspace and expose the
minimum authenticated APIs required to list accessible workspaces, create a
Team Workspace, read one workspace, and rename a Team Workspace.

## Scope

This change adds Personal Workspace provisioning during registration, an
idempotent SQL backfill for existing Sprint 1 users, four workspace endpoints,
and the application and Infrastructure services needed by those endpoints.

It does not add invitations, member administration, configurable permissions,
workspace deletion, branding, billing, projects, tasks, or a success-response
envelope. Centralized resource-based authorization remains SCRUM-34 work.

## Product Rules

- Every account always has one Personal Workspace.
- Registration does not ask the user to choose Personal or Team.
- A Personal Workspace is created automatically and is never converted into a
  Team Workspace.
- Its system-managed name is exactly `Personal Workspace` and cannot be
  changed through the workspace API.
- An authenticated user may create multiple Team Workspaces after
  registration and becomes the Owner of each one.
- Team Workspace names do not need to be unique.
- Users switch workspace context by selecting a returned workspace ID. The
  frontend switcher itself belongs to SCRUM-36.

## Architecture

### Application boundary

Add one `IWorkspaceService` under the Application project. It exposes the four
SCRUM-33 operations using application records and explicit result statuses;
the interface does not depend on EF Core, ASP.NET Core, or Infrastructure
entities.

The service result distinguishes:

- success;
- validation failure;
- inaccessible or missing workspace;
- insufficient role;
- system-managed Personal Workspace name.

This lets the API layer translate business outcomes into HTTP semantics
without parsing database exceptions.

### Infrastructure implementation

Implement `IWorkspaceService` in Infrastructure using the existing
`TempoDeskIdentityDbContext` and SCRUM-32 persistence mappings.

- List and read queries require `removed_at IS NULL` and the authenticated
  caller's user ID.
- Team creation inserts the workspace and Owner membership in one explicit
  transaction and commits only after both rows are valid.
- Rename first scopes by active membership. Team Workspace rename is allowed
  only for `owner` or `admin`; Personal Workspace rename is rejected.
- All writes use `TimeProvider.GetUtcNow()` for timestamps.

The temporary Owner/Admin role check lives in the workspace service as one
focused decision point. SCRUM-34 will replace or delegate it to the shared
authorization service rather than copying it into controllers.

### API layer

Add one authenticated `WorkspacesController`. It derives the caller solely
from the authenticated `ClaimTypes.NameIdentifier` claim and never accepts a
caller user ID in the body, query string, or headers.

The controller validates and normalizes request names, calls
`IWorkspaceService`, and translates results into DTOs or Problem Details. It
does not access the DbContext directly.

## Personal Workspace Provisioning

Add an internal Infrastructure provisioner that ensures a Personal Workspace
and active Owner membership for a user. It uses one PostgreSQL upsert statement
or CTE so repeated and concurrent calls converge without creating duplicates
or surfacing a uniqueness failure.

During registration:

1. Identity creates the user inside the existing registration transaction.
2. The provisioner ensures the Personal Workspace and Owner membership.
3. Email verification token and audit records are saved.
4. The transaction commits once.

Any provisioning failure rolls back user creation, workspace data, token, and
audit data together. The duplicate-registration path may safely call the same
idempotent provisioner for an existing user before returning the generic
accepted response.

The Personal Workspace uses:

- `type = 'personal'`;
- `name = 'Personal Workspace'`;
- the account user as `owner_user_id`;
- one active membership with `role = 'owner'`.

## Existing-User Backfill

Add one forward SQL migration after the SCRUM-32 schema migration. It selects
every existing user without a Personal Workspace, creates the workspace, and
creates the matching Owner membership in the same migration transaction.

The migration is idempotent:

- it does not alter an existing Personal Workspace;
- it relies on the SCRUM-32 partial unique indexes as the final concurrency
  guard;
- rerunning the migration runner records `SKIP` through schema history and
  cannot create duplicate workspace or membership rows.

Random UUIDs are generated for backfilled workspace and membership rows. The
SCRUM-32 deferred Owner-integrity constraint is satisfied before the migration
transaction commits.

## Workspace Name Validation

The API and application boundary apply the same rule to create and rename:

- `name` is required;
- leading and trailing whitespace is removed;
- the trimmed value must contain at least one character;
- the trimmed value must not exceed 200 characters;
- Unicode is accepted;
- duplicate names are accepted.

The database remains the last line of defense through `varchar(200)` and the
existing non-empty check constraint. Requests cannot set workspace type,
owner, role, timestamps, or membership state.

## API Contract

All endpoints require authentication.

### List workspaces

`GET /api/workspaces`

Returns `200 OK` with an array containing only the caller's active
memberships. Each item contains:

```json
{
  "id": "3dfe6f44-9299-4770-85d7-26d2eed6dac4",
  "name": "Personal Workspace",
  "type": "personal",
  "role": "owner"
}
```

The result is ordered with the Personal Workspace first, then Team Workspaces
by name and ID for stable output.

### Create Team Workspace

`POST /api/workspaces`

Request:

```json
{
  "name": "TempoDesk Team"
}
```

Returns `201 Created`, a `Location` header targeting the read endpoint, and the
created workspace DTO. The authenticated caller is the Owner. The client
cannot request another type or role.

### Read workspace

`GET /api/workspaces/{workspaceId}`

Returns `200 OK` and the workspace DTO when the caller has an active
membership. A missing workspace and an inaccessible workspace both return the
same `404 Not Found` response.

### Rename Team Workspace

`PATCH /api/workspaces/{workspaceId}`

Request:

```json
{
  "name": "New team name"
}
```

Returns `200 OK` with the updated workspace DTO. Owner and Admin roles may
rename a Team Workspace. Member and Viewer roles receive `403 Forbidden`.

## Response and Error Contract

Successful responses return endpoint DTOs directly; SCRUM-33 does not add a
generic response envelope. Empty successful operations would use `204`, though
none of the four workspace endpoints requires it.

Errors use the repository's RFC Problem Details contract and include a
`traceId`:

- `400 Bad Request`: missing, blank, or over-200-character name. A
  `ValidationProblemDetails` response places messages under `errors.name`.
- `401 Unauthorized`: missing or invalid authentication.
- `403 Forbidden`: the caller has an active membership but their role cannot
  rename a Team Workspace.
- `404 Not Found`: workspace does not exist or caller has no active
  membership. The response does not reveal which case occurred.
- `409 Conflict`: an attempt to rename a Personal Workspace, with detail
  `Personal Workspace names are managed by the system.`
- `500 Internal Server Error`: unexpected failure with no database, SQL, user,
  or membership details exposed.

The exact validation messages are:

- `Workspace name is required.`
- `Workspace name must not exceed 200 characters.`

## Data Isolation and Security

- Every workspace query includes the caller's user ID and active-membership
  predicate in the database query; filtering is not performed only in memory.
- Team creation derives ownership exclusively from the authenticated caller.
- Read and rename operations cannot be authorized by a role or user ID sent by
  the client.
- The API returns no membership list, invitation state, token material, or
  unrelated account data.
- Frontend visibility is not treated as authorization.

## Verification

SCRUM-33 does not introduce a server test project; SCRUM-37 owns the automated
API, concurrency, and complete permission-matrix infrastructure.

Verification for this ticket uses the real PostgreSQL and API boundaries:

1. Apply the new backfill migration, then rerun the migrator and observe
   `SKIP` without checksum failure.
2. Build the complete server solution in Release configuration with zero
   warnings and errors.
3. Register a new account and verify exactly one Personal Workspace and one
   active Owner membership.
4. Repeat provisioning and verify row counts do not change.
5. Verify existing Sprint 1 users receive exactly one Personal Workspace.
6. Create a Team Workspace and verify the workspace and Owner membership commit
   atomically.
7. With two authenticated accounts, verify list/read/rename isolation.
8. Verify Personal rename returns `409`, valid Team rename returns `200`, and
   invalid names return `400` with `errors.name`.

## Out of Scope

- Invitation and membership administration.
- Ownership transfer and leaving a workspace.
- Centralized workspace authorization policies.
- Workspace switcher UI.
- Workspace branding, avatar, billing, deletion, and retention.
- Projects, tasks, boards, and workspace-owned product data.
- A generic success-response envelope.
