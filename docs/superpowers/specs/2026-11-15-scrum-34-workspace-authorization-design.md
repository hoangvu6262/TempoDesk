# SCRUM-34 Workspace Authorization Design

## Goal

Centralize resource-based workspace authorization so every workspace operation
uses one backend permission model, active membership is the source of truth,
and client-supplied role values never grant access.

This ticket defines the complete authorization vocabulary needed by the Team
Workspace lifecycle, but only applies it to the workspace endpoints that exist
after SCRUM-33. Invitation and membership lifecycle endpoints remain in
SCRUM-35.

## Architecture

The Application project owns the stable workspace actions, permission matrix,
authorization result types, and `IWorkspaceAuthorizationService` contract.
Infrastructure implements that service by loading the caller's active
membership from PostgreSQL and evaluating the Application policy.

Workspace operations call this shared authorization service instead of
comparing role strings. This keeps the permission model usable by future
project/task handlers and non-HTTP callers without depending on ASP.NET Core
authorization types.

The frontend continues to receive the caller's effective role in workspace
responses. That role is read from the database and is informational only; no
request body, query parameter, or header may provide an authoritative caller
role.

## Stable Actions

The policy defines these actions:

- `ViewWorkspace`
- `RenameWorkspace`
- `ViewMembers`
- `InviteMemberOrViewer`
- `InviteAdmin`
- `ChangeMemberOrViewerRole`
- `ChangeAdmin`
- `RemoveMemberOrViewer`
- `RemoveAdmin`
- `TransferOwnership`
- `LeaveWorkspace`

Actions are separated by security significance rather than accepting a target
role supplied by the client. A future membership use case must load the current
target membership and select the appropriate action itself. For example,
promoting a Member to Admin requires `InviteAdmin`, while changing a Member to
Viewer requires `ChangeMemberOrViewerRole`.

## Permission Matrix

| Action | Owner | Admin | Member | Viewer |
| --- | :---: | :---: | :---: | :---: |
| View workspace | Yes | Yes | Yes | Yes |
| Rename Team Workspace | Yes | Yes | No | No |
| View members | Yes | Yes | Yes | Yes |
| Invite Member or Viewer | Yes | Yes | No | No |
| Invite or promote Admin | Yes | No | No | No |
| Change Member/Viewer roles | Yes | Yes | No | No |
| Remove Member or Viewer | Yes | Yes | No | No |
| Change Admin | Yes | No | No | No |
| Remove Admin | Yes | No | No | No |
| Transfer ownership | Yes | No | No | No |
| Leave workspace | No | Yes | Yes | Yes |

An active Owner cannot leave while still Owner. Ownership must be transferred
first, after which the former Owner has a non-Owner role and may leave. Personal
Workspace ownership cannot be transferred. The workspace-type and ownership
transition rules belong to the SCRUM-35 use cases; this ticket establishes the
permission required to attempt them.

Unknown roles and unknown actions are denied by default.

## Authorization Contract and Data Flow

For a resource-scoped request, the caller identity comes only from the
authenticated `NameIdentifier` claim. The workspace operation asks
`IWorkspaceAuthorizationService` to authorize the caller for a workspace ID and
one stable action.

The Infrastructure implementation queries `workspace_members` using all of:

- requested workspace ID;
- authenticated user ID;
- `removed_at IS NULL`.

It then evaluates the role stored on that membership against the fixed matrix.
The result distinguishes:

- `Allowed`, including the effective database role;
- `NotFound`, when no active membership exists;
- `Forbidden`, when an active membership exists but lacks the action.

No active membership is intentionally indistinguishable from a missing
workspace. This prevents missing, removed, and cross-workspace requests from
revealing resource existence or data.

## Existing Endpoint Integration

`GET /api/workspaces` loads only the caller's active memberships and applies
the shared `ViewWorkspace` policy before returning each workspace. It never
returns another user's workspace data.

`GET /api/workspaces/{workspaceId}` requires `ViewWorkspace`. Missing,
removed, and inaccessible resources all return `404 Not Found`.

`PATCH /api/workspaces/{workspaceId}` requires `RenameWorkspace`. Owner and
Admin may proceed; Member and Viewer receive `403 Forbidden`. Missing, removed,
and inaccessible resources return `404 Not Found`. Personal Workspace rename
continues to return the existing `409 Conflict` after authorization succeeds.
The current inline Owner/Admin comparison is removed.

`POST /api/workspaces` remains an authenticated account-level operation because
there is no existing workspace resource to authorize. It continues to create
the Team Workspace and its Owner membership atomically.

## Error and Security Semantics

- A caller with no active membership receives the same `404` response whether
  the workspace is missing, belongs to someone else, or the caller was removed.
- An active member who can know the workspace exists but lacks a requested
  action receives `403`.
- Role values in bodies, query strings, and headers are ignored and cannot
  affect authorization.
- A removed membership loses access on the next authorization query; no role
  claim or cached client state can preserve access.
- Corrupt or unrecognized persisted roles fail closed.

## Testing

Application tests cover every role/action combination in the fixed matrix and
explicitly verify default-deny behavior. Focused tests cover the sensitive
Admin restrictions: Admin cannot invite or promote Admin, change or remove an
Admin, or transfer ownership.

Infrastructure or integration tests verify that the authorization service:

- allows an action supported by an active membership;
- returns `Forbidden` for an active membership without the action;
- returns the same `NotFound` result for missing, removed, and cross-workspace
  memberships;
- returns the effective role stored in the database;
- denies unknown persisted roles.

Workspace endpoint or service regression tests verify list, read, and rename
use the centralized policy and preserve the documented HTTP behavior.

Final verification runs:

```bash
dotnet test server/TempoDesk.sln
dotnet build server/TempoDesk.sln
```

## Out of Scope

- SCRUM-35 invitation and membership lifecycle endpoints.
- Project-level roles.
- Custom roles or per-user overrides.
- Organization-wide administrators.
- Frontend membership management UI.
