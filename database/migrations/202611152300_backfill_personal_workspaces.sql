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
