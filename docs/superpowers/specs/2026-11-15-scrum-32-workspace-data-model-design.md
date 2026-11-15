# SCRUM-32 Workspace and Membership Data Model Design

## Goal

Create the PostgreSQL schema and EF Core persistence mappings for Personal
Workspaces, Team Workspaces, memberships, and invitations. PostgreSQL must
enforce ownership, uniqueness, lifecycle, and referential-integrity rules even
under concurrent writes.

## Scope

This change adds one SQL-first migration and maps the resulting tables into the
existing `TempoDeskIdentityDbContext`. It does not add workspace APIs,
provision Personal Workspaces, implement authorization, or introduce a server
test project. Those responsibilities belong to SCRUM-33, SCRUM-34, and
SCRUM-37.

The permission matrix remains application code. There is no
`workspace_role_permissions` table.

## Architecture

Workspace persistence remains in `TempoDesk.Infrastructure` and uses the
existing identity database context. This keeps user, workspace, membership,
and invitation writes on one EF Core transaction boundary and avoids a second
context that would duplicate the `users` mapping.

The database is authoritative for structural invariants. Persistence entities
contain only mapped state and navigation properties; later application use
cases will own business behavior and authorization decisions.

## Database Model

### `workspaces`

- `id uuid` primary key.
- `name varchar(200)` required.
- `type varchar(20)` restricted to `personal` or `team`.
- `owner_user_id uuid` required and linked to `users(id)`.
- `created_at timestamptz` and `updated_at timestamptz` required.
- A partial unique index on `owner_user_id` where `type = 'personal'` ensures
  one Personal Workspace per owner.
- An index on `owner_user_id` supports owner lookup.

Deleting a user cascades to workspaces they own. Deleting a workspace cascades
to its memberships and invitations, so no dependent row becomes orphaned.

### `workspace_members`

- `id uuid` primary key.
- `workspace_id uuid` required and linked to `workspaces(id)`.
- `user_id uuid` required and linked to `users(id)`.
- `role varchar(20)` restricted to `owner`, `admin`, `member`, or `viewer`.
- `created_at timestamptz` required.
- `removed_at timestamptz` nullable; a null value means the membership is
  active.
- A partial unique index on `(workspace_id, user_id)` where `removed_at IS
  NULL` permits membership history while allowing only one active membership.
- A partial unique index on `workspace_id` where `role = 'owner'` and
  `removed_at IS NULL` allows exactly one active Owner membership.
- Supporting indexes cover active workspace members and a user's active
  memberships.

### `workspace_invitations`

- `id uuid` primary key.
- `workspace_id uuid` required and linked to `workspaces(id)`.
- `invited_by_user_id uuid` required and linked to `users(id)`.
- `email varchar(256)` and `normalized_email varchar(256)` required.
- `role varchar(20)` restricted to `admin`, `member`, or `viewer`; ownership is
  transferred through a separate operation rather than an invitation.
- `token_hash varchar(64)` required and globally unique. No raw invitation
  token column exists.
- `created_at timestamptz` and `expires_at timestamptz` required.
- `accepted_at timestamptz` and `cancelled_at timestamptz` nullable.
- Check constraints require expiry after creation and prevent an invitation
  from being both accepted and cancelled.
- A partial unique index on `(workspace_id, normalized_email)` where both
  lifecycle timestamps are null prevents duplicate active invitations.
- Supporting indexes cover workspace listing, inviter lookup, and expiry
  lookup.

An invitation whose expiry time has passed may still occupy the active unique
index until application code marks it cancelled or removes it from the active
lifecycle. PostgreSQL partial-index predicates cannot safely depend on the
current clock. SCRUM-35 will handle expiry during invitation commands.

## Owner Integrity

Every workspace must have an active Owner membership matching
`workspaces.owner_user_id`. PostgreSQL enforces this with deferrable constraint
triggers on both `workspaces` and `workspace_members`. At transaction commit,
the trigger function checks that each affected workspace still has an active
membership whose user matches `owner_user_id` and whose role is `owner`.

Deferring the check allows workspace creation and its Owner membership in
either statement order inside one transaction. It also permits an atomic
ownership transfer that updates the workspace owner and both membership roles
before commit. Inserting, removing, or changing a membership cannot leave an
existing workspace with a missing, inactive, or mismatched Owner. A membership
trigger caused by cascading workspace deletion succeeds when the workspace no
longer exists.

EF Core maps the relationships needed for normal queries, but the SQL
migration remains the source of truth for these deferred cross-table
constraints because EF Core does not model PostgreSQL constraint triggers.

## Persistence Mapping

Add focused persistence types under
`TempoDesk.Infrastructure/Workspaces/Persistence`:

- `Workspace`
- `WorkspaceMember`
- `WorkspaceInvitation`
- `WorkspacePersistenceConfiguration`

The existing context exposes a `DbSet` for each type and delegates their model
configuration to `WorkspacePersistenceConfiguration`. The mappings use the
exact SQL table, column, constraint, and index names so EF-generated queries
match the SQL-first schema. String constants represent database type and role
values; no configurable role or permission entity is introduced.

## Failure and Lifecycle Behavior

- Invalid workspace types or roles fail with PostgreSQL check-constraint
  violations.
- Concurrent duplicate Personal Workspaces, active memberships, token hashes,
  or active invitations fail through unique indexes.
- User and workspace deletion cannot leave dependent membership or invitation
  rows.
- Membership removal is non-destructive through `removed_at` so later audit
  and membership history can retain identity.
- Invitation acceptance and cancellation retain the invitation row and record
  lifecycle timestamps.

## Verification

SCRUM-32 uses the repository's existing verification boundary:

```bash
dotnet run --project server/tools/TempoDesk.DatabaseMigrator
dotnet build server/TempoDesk.sln
```

The migrator must apply the new file on a configured PostgreSQL database and
skip it on a subsequent run without a checksum mismatch. Full database
integration and concurrency coverage will be introduced in SCRUM-37.

## Out of Scope

- Personal Workspace provisioning and backfill.
- Workspace API endpoints.
- Authorization policies and the permission matrix implementation.
- Invitation delivery or membership lifecycle use cases.
- Configurable roles or permissions.
- Projects, tasks, boards, workspace deletion policy, and retention policy.
