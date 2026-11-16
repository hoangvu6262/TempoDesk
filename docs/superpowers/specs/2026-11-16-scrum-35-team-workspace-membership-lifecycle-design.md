# SCRUM-35 Team Workspace Membership Lifecycle Design

## Goal

Implement the complete backend lifecycle for Team Workspace invitations and
memberships: invite, accept or cancel invitations, list members and pending
invitations, change roles, remove or leave, and transfer ownership without
violating the SCRUM-32 persistence invariants or SCRUM-34 permission matrix.

This change restores one minimal backend test project. It does not add or
modify frontend code.

## Architecture

The Application project defines the membership service contract, operation
results, summaries, invitation email boundary, and email content. The
Infrastructure project implements one `WorkspaceMembershipService` over the
existing `TempoDeskIdentityDbContext`, `IWorkspaceAuthorizationService`,
ASP.NET Identity email normalizer, `TimeProvider`, and invitation sender. The
API project exposes authenticated controllers and maps service results to the
existing Problem Details conventions.

Keeping the lifecycle in one service gives invitation acceptance one database
transaction boundary with membership creation. It also avoids repositories or
cross-service orchestration that would duplicate EF Core behavior. The
existing `WorkspaceService` remains responsible only for workspace profile
operations.

All resource-scoped operations use the SCRUM-34 authorization service and
stable `WorkspaceAction` values. The service chooses the action from persisted
current and target roles. A request body never selects the caller's authority.

## API Contract

The backend exposes the ticket's proposed routes:

- `GET /api/workspaces/{workspaceId}/members`
- `GET /api/workspaces/{workspaceId}/invitations`
- `POST /api/workspaces/{workspaceId}/invitations`
- `DELETE /api/workspaces/{workspaceId}/invitations/{invitationId}`
- `POST /api/workspace-invitations/accept`
- `PATCH /api/workspaces/{workspaceId}/members/{memberId}`
- `DELETE /api/workspaces/{workspaceId}/members/{memberId}`
- `POST /api/workspaces/{workspaceId}/leave`
- `POST /api/workspaces/{workspaceId}/transfer-ownership`

Invitation creation returns `201 Created` with an invitation DTO. Acceptance
and role changes return `200 OK` with the resulting membership DTO. Invitation
cancellation, member removal, leaving, and ownership transfer return `204 No
Content`.

Member DTOs contain only membership ID, email, role, and joined timestamp.
Invitation DTOs contain only invitation ID, email, role, creation timestamp,
and expiry timestamp. No response contains raw tokens, token hashes, unrelated
account profile data, password or security fields, or persistence navigation
objects.

## Invitation Issuance and Delivery

Invitation email is trimmed, syntactically validated, and normalized with the
same ASP.NET Identity `ILookupNormalizer` used by account identity. Roles are
limited to Admin, Member, and Viewer; Owner is reachable only through the
ownership-transfer operation.

The service generates a token from 32 cryptographically secure random bytes,
encodes it as unpadded base64url, and stores only its lowercase SHA-256 hash.
The raw token exists only in memory long enough to pass an invitation message
to `IWorkspaceInvitationSender`. The SMTP implementation builds the acceptance
link from the configured frontend base URL. Neither token form is logged or
returned from the API.

The invitation is committed before SMTP delivery, matching the repository's
existing email boundary. Delivery failure does not roll back the committed
invitation or expose provider details in the response. It also does not log
the message or token. Reminders, retries, an outbox, and scheduled cleanup are
outside SCRUM-35.

Invitation creation runs in a transaction that locks the Team Workspace,
cancels an expired lifecycle-active invitation for the same normalized email,
rejects an existing active member, and inserts the new invitation. The existing
partial unique index on `(workspace_id, normalized_email)` remains the final
concurrency guard. A concurrent uniqueness failure maps to a conflict without
exposing persistence details.

## Invitation Acceptance and Cancellation

Acceptance is authenticated. The service rejects a missing, blank, oversized,
or malformed base64url token before querying persistence. It hashes a valid
token and locks the matching invitation with PostgreSQL `FOR UPDATE`. After
acquiring the lock, it verifies that the invitation is unaccepted,
uncancelled, unexpired, and addressed to the accepting account's normalized
email.

The service creates a new active membership and sets `accepted_at` in one
transaction. A previously removed membership remains as history; acceptance
creates a new row. The database partial unique index permits only one active
membership for the workspace and user. A repeated or concurrent acceptance
waits for the invitation lock, observes the consumed state, and fails without
creating another active membership.

Expired invitations encountered during acceptance are marked cancelled in the
same transaction so they stop occupying the active-email unique slot. Missing,
malformed, expired, cancelled, reused, and wrong-email tokens all return the
same generic invalid-invitation failure.

Cancellation loads the persisted invitation and selects authorization from its
persisted target role. Cancelling an Admin invitation requires `InviteAdmin`;
cancelling a Member or Viewer invitation requires `InviteMemberOrViewer`.
Accepted or already-cancelled invitations are treated as missing. An expired
but otherwise active invitation may be cancelled successfully.

## Listing and Permission Selection

Member and invitation lists require `ViewMembers`, so every active role may
view them exactly as defined by SCRUM-34. The member list contains active
members only. The invitation list contains only unaccepted, uncancelled, and
unexpired invitations.

Role changes derive authorization from both the persisted current role and the
requested target role:

- Member-to-Viewer and Viewer-to-Member use
  `ChangeMemberOrViewerRole`.
- Any promotion to Admin uses `InviteAdmin`.
- Any change from Admin uses `ChangeAdmin`.
- Owner cannot be changed through the role endpoint.

Removal derives authorization from the persisted target role. Removing an
Admin requires `RemoveAdmin`; removing a Member or Viewer requires
`RemoveMemberOrViewer`; removing Owner is always rejected. Admin therefore
cannot alter or remove an Owner or Admin.

Acceptance does not require an existing workspace membership. Authentication
plus normalized-email ownership is its authorization boundary.

## Removal, Leaving, and Ownership Transfer

Removal and leaving set `removed_at` instead of deleting membership history.
Authorization queries already require `removed_at IS NULL`, so access is lost
on the next request. An Owner cannot be removed and the SCRUM-34 policy denies
leaving while the caller is Owner.

Ownership transfer requires `TransferOwnership`, a Team Workspace, and an
active non-Owner target membership. The service locks the workspace and the
current and target memberships. In one transaction it changes
`workspaces.owner_user_id`, changes the target role to Owner, and changes the
former Owner role to Admin. The existing deferred owner-integrity constraint
validates the final state at commit. Locking the workspace serializes competing
transfer, removal, and role-change operations around ownership.

Personal Workspace mutation is rejected for invitation creation or
cancellation, invitation acceptance, role changes, removal, leaving, and
ownership transfer. Read-only member and invitation listing remains governed
by `ViewMembers`; a Personal Workspace has one member and no invitations.

## Error Semantics

- A missing workspace, inaccessible workspace, or removed caller membership
  returns `404 Not Found` without revealing resource existence.
- An active member who lacks the selected SCRUM-34 action receives `403
  Forbidden`.
- Invalid email, role, request shape, or membership target produces the
  existing validation-style `400 Bad Request` response.
- Every invalid invitation token condition produces the same generic `400 Bad
  Request` response.
- An active duplicate invitation, an already-active member invitation, or a
  lifecycle mutation forbidden for a Personal Workspace returns `409
  Conflict`.
- Database constraint names, SQL, token material, and SMTP details never
  appear in responses or application logs.

## Testing

Restore one backend-only `TempoDesk.Tests` xUnit project. It references the
Application, Infrastructure, and API projects and uses PostgreSQL Testcontainers
for behaviors that depend on partial indexes, row locks, transactions, and
deferred owner constraints.

The test suite covers:

- SCRUM-34 action selection for every caller role and target-role transition;
- invitation email normalization, template output, and token-input validation;
- Owner/Admin invitation boundaries and Personal Workspace rejection;
- active invitation uniqueness and replacement of an expired invitation;
- missing, malformed, expired, cancelled, reused, and wrong-email acceptance;
- exactly one active membership under repeated and concurrent acceptance;
- DTO and serialized-response absence of token/hash/unrelated account fields;
- immediate access loss after removal or leaving;
- Admin inability to manage Owner or Admin;
- Owner removal and leave prevention;
- successful and competing atomic ownership transfer; and
- API authentication, status codes, and Problem Details mapping.

Final verification runs:

```bash
dotnet test server/TempoDesk.sln
dotnet build server/TempoDesk.sln
```

A source and response-contract scan verifies that raw token and token-hash
properties are not present in logs or public DTOs.

## Out of Scope

- Frontend membership management or invitation acceptance pages.
- Anonymous invitation acceptance.
- Reminder emails, scheduled expiry cleanup, delivery retry, or an email
  outbox.
- Bulk invitations, CSV import, SCIM, and domain-based automatic membership.
- Custom roles or a configurable permission table.
- Workspace deletion or membership-history retention policy.
