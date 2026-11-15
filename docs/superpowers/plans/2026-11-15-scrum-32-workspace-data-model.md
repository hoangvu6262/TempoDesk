# SCRUM-32 Workspace Data Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the SQL-first workspace, membership, and invitation schema plus matching EF Core persistence mappings.

**Architecture:** PostgreSQL owns structural and concurrency invariants through check constraints, partial unique indexes, foreign keys, and deferred constraint triggers. The existing `TempoDeskIdentityDbContext` remains the single transaction boundary and delegates workspace mappings to one focused Infrastructure configuration class.

**Tech Stack:** PostgreSQL 18, SQL-first migration runner, .NET 10, EF Core 10, Npgsql 10.

## Global Constraints

- Keep the permission matrix in application code; do not create `workspace_role_permissions`.
- Store invitation-token hashes only; never add a raw invitation-token column.
- Use UTC-compatible `timestamptz` columns and `DateTimeOffset` properties.
- Keep business behavior out of persistence entities.
- Do not add workspace APIs, Personal Workspace provisioning, or authorization policies.
- Do not add a server test project in SCRUM-32; use PostgreSQL smoke checks and the existing build boundary. Full automated integration and concurrency coverage belongs to SCRUM-37.
- Do not edit an already-applied migration; all schema work goes in the new migration.

---

## File Structure

- Create `database/migrations/202611152130_create_workspace_schema.sql`: tables, constraints, indexes, and deferred Owner-integrity triggers.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/Workspace.cs`: workspace persistence state and navigations.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceMember.cs`: membership persistence state and navigations.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceInvitation.cs`: invitation persistence state and navigations.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceTypeValues.cs`: stable `personal` and `team` values.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceRoleValues.cs`: stable `owner`, `admin`, `member`, and `viewer` values.
- Create `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspacePersistenceConfiguration.cs`: all EF Core workspace mappings.
- Modify `server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs`: expose the three sets and apply their configuration.

### Task 1: Add the PostgreSQL workspace schema

**Files:**

- Create: `database/migrations/202611152130_create_workspace_schema.sql`

**Interfaces:**

- Consumes: `public.users(id)` from Sprint 1 and the SQL-first migration runner.
- Produces: `public.workspaces`, `public.workspace_members`, and `public.workspace_invitations` with database-enforced invariants.

- [ ] **Step 1: Verify the schema is absent before implementation**

Run:

```bash
docker compose up -d postgres
docker compose exec -T postgres psql -U postgres -d tempodesk -v ON_ERROR_STOP=1 \
  -c "SELECT * FROM public.workspaces LIMIT 1;"
```

Expected: PostgreSQL fails with `relation "public.workspaces" does not exist`.
If `.env` uses a database name other than `tempodesk`, substitute its
`POSTGRES_DB` value without printing any password.

- [ ] **Step 2: Create the migration**

Create `database/migrations/202611152130_create_workspace_schema.sql` with:

```sql
-- Rollback before production data exists: drop the constraint triggers and
-- functions, then drop workspace_invitations, workspace_members, and workspaces.
-- After release, use a new forward migration instead of editing this file.

CREATE TABLE public.workspaces (
    id uuid PRIMARY KEY,
    name varchar(200) NOT NULL,
    type varchar(20) NOT NULL,
    owner_user_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_workspaces_owner
        FOREIGN KEY (owner_user_id)
        REFERENCES public.users (id)
        ON DELETE CASCADE,
    CONSTRAINT ck_workspaces_name_not_empty
        CHECK (btrim(name) <> ''),
    CONSTRAINT ck_workspaces_type
        CHECK (type IN ('personal', 'team')),
    CONSTRAINT ck_workspaces_updated_at
        CHECK (updated_at >= created_at)
);

CREATE UNIQUE INDEX ux_workspaces_personal_owner
    ON public.workspaces (owner_user_id)
    WHERE type = 'personal';

CREATE INDEX ix_workspaces_owner_user_id
    ON public.workspaces (owner_user_id);


CREATE TABLE public.workspace_members (
    id uuid PRIMARY KEY,
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    role varchar(20) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    removed_at timestamptz,

    CONSTRAINT fk_workspace_members_workspace
        FOREIGN KEY (workspace_id)
        REFERENCES public.workspaces (id)
        ON DELETE CASCADE
        DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT fk_workspace_members_user
        FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON DELETE CASCADE,
    CONSTRAINT ck_workspace_members_role
        CHECK (role IN ('owner', 'admin', 'member', 'viewer')),
    CONSTRAINT ck_workspace_members_removed_at
        CHECK (removed_at IS NULL OR removed_at >= created_at)
);

CREATE UNIQUE INDEX ux_workspace_members_active_user
    ON public.workspace_members (workspace_id, user_id)
    WHERE removed_at IS NULL;

CREATE UNIQUE INDEX ux_workspace_members_active_owner
    ON public.workspace_members (workspace_id)
    WHERE role = 'owner' AND removed_at IS NULL;

CREATE INDEX ix_workspace_members_workspace_active
    ON public.workspace_members (workspace_id)
    WHERE removed_at IS NULL;

CREATE INDEX ix_workspace_members_user_active
    ON public.workspace_members (user_id)
    WHERE removed_at IS NULL;


CREATE TABLE public.workspace_invitations (
    id uuid PRIMARY KEY,
    workspace_id uuid NOT NULL,
    invited_by_user_id uuid NOT NULL,
    email varchar(256) NOT NULL,
    normalized_email varchar(256) NOT NULL,
    role varchar(20) NOT NULL,
    token_hash varchar(64) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at timestamptz NOT NULL,
    accepted_at timestamptz,
    cancelled_at timestamptz,

    CONSTRAINT fk_workspace_invitations_workspace
        FOREIGN KEY (workspace_id)
        REFERENCES public.workspaces (id)
        ON DELETE CASCADE,
    CONSTRAINT fk_workspace_invitations_inviter
        FOREIGN KEY (invited_by_user_id)
        REFERENCES public.users (id)
        ON DELETE CASCADE,
    CONSTRAINT ck_workspace_invitations_email_not_empty
        CHECK (btrim(email) <> ''),
    CONSTRAINT ck_workspace_invitations_normalized_email_not_empty
        CHECK (btrim(normalized_email) <> ''),
    CONSTRAINT ck_workspace_invitations_role
        CHECK (role IN ('admin', 'member', 'viewer')),
    CONSTRAINT ck_workspace_invitations_token_hash
        CHECK (token_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_workspace_invitations_expiry
        CHECK (expires_at > created_at),
    CONSTRAINT ck_workspace_invitations_accepted_at
        CHECK (accepted_at IS NULL OR accepted_at >= created_at),
    CONSTRAINT ck_workspace_invitations_cancelled_at
        CHECK (cancelled_at IS NULL OR cancelled_at >= created_at),
    CONSTRAINT ck_workspace_invitations_single_outcome
        CHECK (accepted_at IS NULL OR cancelled_at IS NULL)
);

CREATE UNIQUE INDEX ux_workspace_invitations_token_hash
    ON public.workspace_invitations (token_hash);

CREATE UNIQUE INDEX ux_workspace_invitations_active_email
    ON public.workspace_invitations (workspace_id, normalized_email)
    WHERE accepted_at IS NULL AND cancelled_at IS NULL;

CREATE INDEX ix_workspace_invitations_workspace_id
    ON public.workspace_invitations (workspace_id);

CREATE INDEX ix_workspace_invitations_invited_by_user_id
    ON public.workspace_invitations (invited_by_user_id);

CREATE INDEX ix_workspace_invitations_expires_at
    ON public.workspace_invitations (expires_at);


CREATE FUNCTION public.assert_workspace_owner_membership(
    workspace_id_to_check uuid
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM public.workspaces AS workspace
        WHERE workspace.id = workspace_id_to_check
    ) AND NOT EXISTS (
        SELECT 1
        FROM public.workspaces AS workspace
        JOIN public.workspace_members AS member
          ON member.workspace_id = workspace.id
         AND member.user_id = workspace.owner_user_id
         AND member.role = 'owner'
         AND member.removed_at IS NULL
        WHERE workspace.id = workspace_id_to_check
    ) THEN
        RAISE EXCEPTION 'Workspace % requires an active Owner membership',
            workspace_id_to_check
            USING ERRCODE = '23514',
                  CONSTRAINT = 'ck_workspaces_owner_membership';
    END IF;
END;
$$;

CREATE FUNCTION public.validate_workspace_owner_from_workspace()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    PERFORM public.assert_workspace_owner_membership(NEW.id);
    RETURN NEW;
END;
$$;

CREATE FUNCTION public.validate_workspace_owner_from_member()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        PERFORM public.assert_workspace_owner_membership(OLD.workspace_id);
        RETURN OLD;
    END IF;

    IF TG_OP = 'UPDATE' AND OLD.workspace_id <> NEW.workspace_id THEN
        PERFORM public.assert_workspace_owner_membership(OLD.workspace_id);
    END IF;

    PERFORM public.assert_workspace_owner_membership(NEW.workspace_id);
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER ck_workspaces_owner_membership_workspace
AFTER INSERT OR UPDATE ON public.workspaces
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW
EXECUTE FUNCTION public.validate_workspace_owner_from_workspace();

CREATE CONSTRAINT TRIGGER ck_workspaces_owner_membership_member
AFTER INSERT OR UPDATE OR DELETE ON public.workspace_members
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW
EXECUTE FUNCTION public.validate_workspace_owner_from_member();
```

- [ ] **Step 3: Apply the migration**

Run:

```bash
docker compose run --build --rm migrator
```

Expected: output includes
`APPLY 202611152130_create_workspace_schema.sql` and exits `0`.

- [ ] **Step 4: Run focused PostgreSQL smoke checks**

Run this against the Compose PostgreSQL service. The script uses a transaction
and rolls back all fixture rows:

```bash
docker compose exec -T postgres psql -U postgres -d tempodesk -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;

INSERT INTO public.users (
    id, user_name, normalized_user_name, email, normalized_email,
    email_confirmed, lockout_enabled, access_failed_count
) VALUES
    ('00000000-0000-0000-0000-000000000001', 'owner', 'OWNER',
     'owner@example.test', 'OWNER@EXAMPLE.TEST', true, true, 0),
    ('00000000-0000-0000-0000-000000000002', 'member', 'MEMBER',
     'member@example.test', 'MEMBER@EXAMPLE.TEST', true, true, 0);

INSERT INTO public.workspaces (id, name, type, owner_user_id)
VALUES (
    '10000000-0000-0000-0000-000000000001',
    'Owner personal workspace',
    'personal',
    '00000000-0000-0000-0000-000000000001'
);

INSERT INTO public.workspace_members (
    id, workspace_id, user_id, role
) VALUES (
    '20000000-0000-0000-0000-000000000001',
    '10000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000001',
    'owner'
);

INSERT INTO public.workspace_invitations (
    id, workspace_id, invited_by_user_id, email, normalized_email,
    role, token_hash, expires_at
) VALUES (
    '30000000-0000-0000-0000-000000000001',
    '10000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000001',
    'member@example.test',
    'MEMBER@EXAMPLE.TEST',
    'member',
    repeat('a', 64),
    CURRENT_TIMESTAMP + interval '1 day'
);

SET CONSTRAINTS ALL IMMEDIATE;

DO $$
BEGIN
    BEGIN
        INSERT INTO public.workspaces (id, name, type, owner_user_id)
        VALUES (
            '10000000-0000-0000-0000-000000000003',
            'Invalid type',
            'invalid',
            '00000000-0000-0000-0000-000000000002'
        );
        RAISE EXCEPTION 'invalid workspace type was accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO public.workspace_members (
            id, workspace_id, user_id, role
        ) VALUES (
            '20000000-0000-0000-0000-000000000003',
            '10000000-0000-0000-0000-000000000001',
            '00000000-0000-0000-0000-000000000002',
            'invalid'
        );
        RAISE EXCEPTION 'invalid membership role was accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO public.workspaces (id, name, type, owner_user_id)
        VALUES (
            '10000000-0000-0000-0000-000000000004',
            'Missing owner membership',
            'team',
            '00000000-0000-0000-0000-000000000002'
        );
        RAISE EXCEPTION 'workspace without Owner membership was accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO public.workspaces (id, name, type, owner_user_id)
        VALUES (
            '10000000-0000-0000-0000-000000000002',
            'Duplicate personal',
            'personal',
            '00000000-0000-0000-0000-000000000001'
        );
        RAISE EXCEPTION 'duplicate Personal Workspace was accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO public.workspace_members (
            id, workspace_id, user_id, role
        ) VALUES (
            '20000000-0000-0000-0000-000000000002',
            '10000000-0000-0000-0000-000000000001',
            '00000000-0000-0000-0000-000000000001',
            'viewer'
        );
        RAISE EXCEPTION 'duplicate active membership was accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO public.workspace_invitations (
            id, workspace_id, invited_by_user_id, email, normalized_email,
            role, token_hash, expires_at
        ) VALUES (
            '30000000-0000-0000-0000-000000000002',
            '10000000-0000-0000-0000-000000000001',
            '00000000-0000-0000-0000-000000000001',
            'member@example.test',
            'MEMBER@EXAMPLE.TEST',
            'viewer',
            repeat('b', 64),
            CURRENT_TIMESTAMP + interval '1 day'
        );
        RAISE EXCEPTION 'duplicate active invitation was accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;

    BEGIN
        UPDATE public.workspace_members
        SET removed_at = CURRENT_TIMESTAMP
        WHERE id = '20000000-0000-0000-0000-000000000001';
        RAISE EXCEPTION 'removal of the active Owner was accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
END;
$$;

DELETE FROM public.users
WHERE id = '00000000-0000-0000-0000-000000000001';

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM public.workspaces
        WHERE owner_user_id = '00000000-0000-0000-0000-000000000001'
    ) OR EXISTS (
        SELECT 1 FROM public.workspace_members
        WHERE user_id = '00000000-0000-0000-0000-000000000001'
    ) OR EXISTS (
        SELECT 1 FROM public.workspace_invitations
        WHERE invited_by_user_id = '00000000-0000-0000-0000-000000000001'
    ) THEN
        RAISE EXCEPTION 'user deletion left orphaned workspace data';
    END IF;
END;
$$;

ROLLBACK;
SQL
```

Expected: `ROLLBACK` with exit code `0`; none of the explicit “was accepted”
exceptions occurs.

- [ ] **Step 5: Commit the migration**

```bash
git add database/migrations/202611152130_create_workspace_schema.sql
git commit -m "feat(db): add workspace persistence schema"
```

### Task 2: Add workspace persistence entities and stable values

**Files:**

- Create: `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/Workspace.cs`
- Create: `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceMember.cs`
- Create: `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceInvitation.cs`
- Create: `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceTypeValues.cs`
- Create: `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspaceRoleValues.cs`

**Interfaces:**

- Consumes: `TempoDesk.Infrastructure.Identity.ApplicationUser`.
- Produces: three persistence types and stable string values consumed by the EF mapping and later workspace repositories.

- [ ] **Step 1: Confirm the expected types do not exist**

Run:

```bash
rg -n "class Workspace|class WorkspaceMember|class WorkspaceInvitation" \
  server/src/TempoDesk.Infrastructure
```

Expected: no matches and exit code `1`.

- [ ] **Step 2: Add the stable database values**

Create `WorkspaceTypeValues.cs`:

```csharp
namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public static class WorkspaceTypeValues
{
    public const string Personal = "personal";
    public const string Team = "team";
}
```

Create `WorkspaceRoleValues.cs`:

```csharp
namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public static class WorkspaceRoleValues
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";
    public const string Viewer = "viewer";
}
```

- [ ] **Step 3: Add the three persistence entities**

Create `Workspace.cs`:

```csharp
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public sealed class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public Guid OwnerUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ApplicationUser Owner { get; set; } = null!;
    public ICollection<WorkspaceMember> Members { get; set; } = [];
    public ICollection<WorkspaceInvitation> Invitations { get; set; } = [];
}
```

Create `WorkspaceMember.cs`:

```csharp
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public sealed class WorkspaceMember
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
```

Create `WorkspaceInvitation.cs`:

```csharp
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public sealed class WorkspaceInvitation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InvitedByUserId { get; set; }
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string Role { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public ApplicationUser InvitedByUser { get; set; } = null!;
}
```

- [ ] **Step 4: Build the Infrastructure project**

Run:

```bash
dotnet build server/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 5: Commit the persistence types**

```bash
git add server/src/TempoDesk.Infrastructure/Workspaces/Persistence
git commit -m "feat(workspaces): add persistence entities"
```

### Task 3: Map the workspace schema in EF Core

**Files:**

- Create: `server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspacePersistenceConfiguration.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs`

**Interfaces:**

- Consumes: the Task 2 persistence types and `ModelBuilder`.
- Produces: `Configure(ModelBuilder)` and `DbSet` properties for all three workspace tables.

- [ ] **Step 1: Add the EF configuration class**

Create `WorkspacePersistenceConfiguration.cs` with one public entry point and
three private table mappings:

```csharp
using Microsoft.EntityFrameworkCore;

namespace TempoDesk.Infrastructure.Workspaces.Persistence;

public static class WorkspacePersistenceConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        ConfigureWorkspaces(builder);
        ConfigureMembers(builder);
        ConfigureInvitations(builder);
    }

    private static void ConfigureWorkspaces(ModelBuilder builder)
    {
        builder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_workspaces_name_not_empty", "btrim(name) <> ''");
                table.HasCheckConstraint(
                    "ck_workspaces_type",
                    "type IN ('personal', 'team')");
                table.HasCheckConstraint(
                    "ck_workspaces_updated_at", "updated_at >= created_at");
            });
            entity.HasKey(workspace => workspace.Id);
            entity.Property(workspace => workspace.Id).HasColumnName("id");
            entity.Property(workspace => workspace.Name)
                .HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(workspace => workspace.Type)
                .HasColumnName("type").HasMaxLength(20).IsRequired();
            entity.Property(workspace => workspace.OwnerUserId)
                .HasColumnName("owner_user_id");
            entity.Property(workspace => workspace.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(workspace => workspace.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(workspace => workspace.Owner)
                .WithMany()
                .HasForeignKey(workspace => workspace.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspaces_owner");
            entity.HasIndex(
                    workspace => workspace.OwnerUserId,
                    "IX_Workspaces_OwnerUserId")
                .HasDatabaseName("ix_workspaces_owner_user_id");
            entity.HasIndex(
                    workspace => workspace.OwnerUserId,
                    "UX_Workspaces_PersonalOwner")
                .HasDatabaseName("ux_workspaces_personal_owner")
                .HasFilter("type = 'personal'")
                .IsUnique();
        });
    }

    private static void ConfigureMembers(ModelBuilder builder)
    {
        builder.Entity<WorkspaceMember>(entity =>
        {
            entity.ToTable("workspace_members", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_workspace_members_role",
                    "role IN ('owner', 'admin', 'member', 'viewer')");
                table.HasCheckConstraint(
                    "ck_workspace_members_removed_at",
                    "removed_at IS NULL OR removed_at >= created_at");
            });
            entity.HasKey(member => member.Id);
            entity.Property(member => member.Id).HasColumnName("id");
            entity.Property(member => member.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(member => member.UserId).HasColumnName("user_id");
            entity.Property(member => member.Role)
                .HasColumnName("role").HasMaxLength(20).IsRequired();
            entity.Property(member => member.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(member => member.RemovedAt)
                .HasColumnName("removed_at");
            entity.HasOne(member => member.Workspace)
                .WithMany(workspace => workspace.Members)
                .HasForeignKey(member => member.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspace_members_workspace");
            entity.HasOne(member => member.User)
                .WithMany()
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspace_members_user");
            entity.HasIndex(member => new
                { member.WorkspaceId, member.UserId })
                .HasDatabaseName("ux_workspace_members_active_user")
                .HasFilter("removed_at IS NULL")
                .IsUnique();
            entity.HasIndex(
                    member => member.WorkspaceId,
                    "UX_WorkspaceMembers_ActiveOwner")
                .HasDatabaseName("ux_workspace_members_active_owner")
                .HasFilter("role = 'owner' AND removed_at IS NULL")
                .IsUnique();
            entity.HasIndex(
                    member => member.WorkspaceId,
                    "IX_WorkspaceMembers_WorkspaceActive")
                .HasDatabaseName("ix_workspace_members_workspace_active")
                .HasFilter("removed_at IS NULL");
            entity.HasIndex(member => member.UserId)
                .HasDatabaseName("ix_workspace_members_user_active")
                .HasFilter("removed_at IS NULL");
        });
    }

    private static void ConfigureInvitations(ModelBuilder builder)
    {
        builder.Entity<WorkspaceInvitation>(entity =>
        {
            entity.ToTable("workspace_invitations", "public", table =>
            {
                table.HasCheckConstraint(
                    "ck_workspace_invitations_email_not_empty",
                    "btrim(email) <> ''");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_normalized_email_not_empty",
                    "btrim(normalized_email) <> ''");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_role",
                    "role IN ('admin', 'member', 'viewer')");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_token_hash",
                    "token_hash ~ '^[0-9a-f]{64}$'");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_expiry",
                    "expires_at > created_at");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_accepted_at",
                    "accepted_at IS NULL OR accepted_at >= created_at");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_cancelled_at",
                    "cancelled_at IS NULL OR cancelled_at >= created_at");
                table.HasCheckConstraint(
                    "ck_workspace_invitations_single_outcome",
                    "accepted_at IS NULL OR cancelled_at IS NULL");
            });
            entity.HasKey(invitation => invitation.Id);
            entity.Property(invitation => invitation.Id).HasColumnName("id");
            entity.Property(invitation => invitation.WorkspaceId)
                .HasColumnName("workspace_id");
            entity.Property(invitation => invitation.InvitedByUserId)
                .HasColumnName("invited_by_user_id");
            entity.Property(invitation => invitation.Email)
                .HasColumnName("email").HasMaxLength(256).IsRequired();
            entity.Property(invitation => invitation.NormalizedEmail)
                .HasColumnName("normalized_email")
                .HasMaxLength(256).IsRequired();
            entity.Property(invitation => invitation.Role)
                .HasColumnName("role").HasMaxLength(20).IsRequired();
            entity.Property(invitation => invitation.TokenHash)
                .HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.Property(invitation => invitation.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(invitation => invitation.ExpiresAt)
                .HasColumnName("expires_at");
            entity.Property(invitation => invitation.AcceptedAt)
                .HasColumnName("accepted_at");
            entity.Property(invitation => invitation.CancelledAt)
                .HasColumnName("cancelled_at");
            entity.HasOne(invitation => invitation.Workspace)
                .WithMany(workspace => workspace.Invitations)
                .HasForeignKey(invitation => invitation.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspace_invitations_workspace");
            entity.HasOne(invitation => invitation.InvitedByUser)
                .WithMany()
                .HasForeignKey(invitation => invitation.InvitedByUserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_workspace_invitations_inviter");
            entity.HasIndex(invitation => invitation.TokenHash)
                .HasDatabaseName("ux_workspace_invitations_token_hash")
                .IsUnique();
            entity.HasIndex(invitation => new
                { invitation.WorkspaceId, invitation.NormalizedEmail })
                .HasDatabaseName("ux_workspace_invitations_active_email")
                .HasFilter("accepted_at IS NULL AND cancelled_at IS NULL")
                .IsUnique();
            entity.HasIndex(invitation => invitation.WorkspaceId)
                .HasDatabaseName("ix_workspace_invitations_workspace_id");
            entity.HasIndex(invitation => invitation.InvitedByUserId)
                .HasDatabaseName(
                    "ix_workspace_invitations_invited_by_user_id");
            entity.HasIndex(invitation => invitation.ExpiresAt)
                .HasDatabaseName("ix_workspace_invitations_expires_at");
        });
    }
}
```

- [ ] **Step 2: Wire the mappings into the existing context**

Add this using to `TempoDeskIdentityDbContext.cs`:

```csharp
using TempoDesk.Infrastructure.Workspaces.Persistence;
```

Add these properties after the existing `DbSet` properties:

```csharp
public DbSet<Workspace> Workspaces => Set<Workspace>();
public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();
public DbSet<WorkspaceInvitation> WorkspaceInvitations =>
    Set<WorkspaceInvitation>();
```

Add this call at the end of `OnModelCreating`:

```csharp
WorkspacePersistenceConfiguration.Configure(builder);
```

- [ ] **Step 3: Build the solution**

Run:

```bash
dotnet build server/TempoDesk.sln
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 4: Commit the EF mapping**

```bash
git add \
  server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs \
  server/src/TempoDesk.Infrastructure/Workspaces/Persistence/WorkspacePersistenceConfiguration.cs
git commit -m "feat(workspaces): map workspace persistence schema"
```

### Task 4: Verify migration repeatability and final scope

**Files:**

- Verify only; no new files expected.

**Interfaces:**

- Consumes: all outputs from Tasks 1–3.
- Produces: fresh evidence that the migration is repeatable, mappings compile, and the diff remains inside SCRUM-32.

- [ ] **Step 1: Run the migration again**

```bash
docker compose run --build --rm migrator
```

Expected: output includes
`SKIP 202611152130_create_workspace_schema.sql` and exits `0`.

- [ ] **Step 2: Run the complete server build**

```bash
dotnet build server/TempoDesk.sln --configuration Release
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 3: Check migration and source formatting**

```bash
git diff --check
```

Expected: no output and exit code `0`.

- [ ] **Step 4: Review the final diff against the ticket**

```bash
git status --short
git diff --stat HEAD~3..HEAD
git diff HEAD~3..HEAD -- \
  database/migrations \
  server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs \
  server/src/TempoDesk.Infrastructure/Workspaces/Persistence
```

Confirm explicitly:

- The three required tables exist.
- Invalid workspace types and membership/invitation roles are rejected.
- Personal Workspace, active membership, active Owner, invitation hash, and
  active invitation uniqueness are database-enforced.
- Owner membership is checked at transaction commit.
- All user/workspace dependents have explicit delete behavior.
- No raw token or `workspace_role_permissions` table exists.
- No API, authorization, project/task, or workspace-deletion behavior was
  added.

- [ ] **Step 5: Record any verification-only fixes in a focused commit**

If verification required a correction, stage only the SCRUM-32 files and use:

```bash
git commit -m "fix(workspaces): align persistence constraints"
```

If there was no correction, do not create an empty commit.
