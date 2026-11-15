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
