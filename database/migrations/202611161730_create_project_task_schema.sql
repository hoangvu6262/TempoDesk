-- Rollback before production data exists: drop task_activity_logs,
-- task_comments, task_labels, labels, task_assignees, tasks, and projects,
-- then drop set_task_update_metadata(). After release, use a new forward
-- migration instead of editing this file.

CREATE TABLE public.projects (
    id uuid PRIMARY KEY,
    workspace_id uuid NOT NULL,
    name varchar(200) NOT NULL,
    description text,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    archived_at timestamptz,

    CONSTRAINT fk_projects_workspace
        FOREIGN KEY (workspace_id)
        REFERENCES public.workspaces (id)
        ON DELETE CASCADE,
    CONSTRAINT uq_projects_workspace_id
        UNIQUE (workspace_id, id),
    CONSTRAINT ck_projects_name_not_empty
        CHECK (btrim(name) <> ''),
    CONSTRAINT ck_projects_updated_at
        CHECK (updated_at >= created_at),
    CONSTRAINT ck_projects_archived_at
        CHECK (archived_at IS NULL OR archived_at >= created_at)
);

CREATE INDEX ix_projects_workspace_active
    ON public.projects (workspace_id, name, id)
    WHERE archived_at IS NULL;


CREATE TABLE public.tasks (
    id uuid PRIMARY KEY,
    workspace_id uuid NOT NULL,
    project_id uuid NOT NULL,
    parent_task_id uuid,
    created_by_user_id uuid NOT NULL,
    title varchar(300) NOT NULL,
    description text,
    priority varchar(20) NOT NULL DEFAULT 'none',
    status varchar(20) NOT NULL DEFAULT 'todo',
    start_date date,
    due_date date,
    estimated_minutes integer,
    source_type varchar(20) NOT NULL DEFAULT 'manual',
    sort_key numeric(30, 15) NOT NULL DEFAULT 0,
    row_version bigint NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    deleted_at timestamptz,

    CONSTRAINT fk_tasks_project
        FOREIGN KEY (workspace_id, project_id)
        REFERENCES public.projects (workspace_id, id)
        ON DELETE CASCADE,
    CONSTRAINT fk_tasks_creator
        FOREIGN KEY (created_by_user_id)
        REFERENCES public.users (id)
        ON DELETE RESTRICT,
    CONSTRAINT uq_tasks_workspace_id
        UNIQUE (workspace_id, id),
    CONSTRAINT uq_tasks_workspace_project_id
        UNIQUE (workspace_id, project_id, id),
    CONSTRAINT fk_tasks_parent
        FOREIGN KEY (workspace_id, project_id, parent_task_id)
        REFERENCES public.tasks (workspace_id, project_id, id)
        ON DELETE CASCADE,
    CONSTRAINT ck_tasks_not_self_parent
        CHECK (parent_task_id IS NULL OR parent_task_id <> id),
    CONSTRAINT ck_tasks_title_not_empty
        CHECK (btrim(title) <> ''),
    CONSTRAINT ck_tasks_priority
        CHECK (priority IN ('none', 'low', 'medium', 'high', 'urgent')),
    CONSTRAINT ck_tasks_status
        CHECK (status IN ('todo', 'in_progress', 'done')),
    CONSTRAINT ck_tasks_source_type
        CHECK (source_type IN ('manual', 'jira')),
    CONSTRAINT ck_tasks_dates
        CHECK (
            start_date IS NULL OR
            due_date IS NULL OR
            due_date >= start_date
        ),
    CONSTRAINT ck_tasks_estimated_minutes
        CHECK (estimated_minutes IS NULL OR estimated_minutes > 0),
    CONSTRAINT ck_tasks_row_version
        CHECK (row_version > 0),
    CONSTRAINT ck_tasks_updated_at
        CHECK (updated_at >= created_at),
    CONSTRAINT ck_tasks_deleted_at
        CHECK (deleted_at IS NULL OR deleted_at >= created_at)
);

CREATE INDEX ix_tasks_workspace_project_active
    ON public.tasks (workspace_id, project_id, sort_key, id)
    WHERE deleted_at IS NULL;

CREATE INDEX ix_tasks_parent_active
    ON public.tasks (parent_task_id, sort_key, id)
    WHERE parent_task_id IS NOT NULL AND deleted_at IS NULL;

CREATE INDEX ix_tasks_workspace_due_active
    ON public.tasks (workspace_id, due_date, id)
    WHERE due_date IS NOT NULL AND deleted_at IS NULL;


CREATE TABLE public.task_assignees (
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    user_id uuid NOT NULL,
    assigned_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT pk_task_assignees
        PRIMARY KEY (task_id, user_id),
    CONSTRAINT fk_task_assignees_task
        FOREIGN KEY (workspace_id, task_id)
        REFERENCES public.tasks (workspace_id, id)
        ON DELETE CASCADE,
    CONSTRAINT fk_task_assignees_user
        FOREIGN KEY (user_id)
        REFERENCES public.users (id)
        ON DELETE RESTRICT
);

CREATE INDEX ix_task_assignees_user_workspace
    ON public.task_assignees (user_id, workspace_id, task_id);


CREATE TABLE public.labels (
    id uuid PRIMARY KEY,
    workspace_id uuid NOT NULL,
    name varchar(100) NOT NULL,
    normalized_name varchar(100) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_labels_workspace
        FOREIGN KEY (workspace_id)
        REFERENCES public.workspaces (id)
        ON DELETE CASCADE,
    CONSTRAINT uq_labels_workspace_id
        UNIQUE (workspace_id, id),
    CONSTRAINT ck_labels_name_not_empty
        CHECK (btrim(name) <> ''),
    CONSTRAINT ck_labels_normalized_name_not_empty
        CHECK (btrim(normalized_name) <> '')
);

CREATE UNIQUE INDEX ux_labels_workspace_normalized_name
    ON public.labels (workspace_id, normalized_name);


CREATE TABLE public.task_labels (
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    label_id uuid NOT NULL,

    CONSTRAINT pk_task_labels
        PRIMARY KEY (task_id, label_id),
    CONSTRAINT fk_task_labels_task
        FOREIGN KEY (workspace_id, task_id)
        REFERENCES public.tasks (workspace_id, id)
        ON DELETE CASCADE,
    CONSTRAINT fk_task_labels_label
        FOREIGN KEY (workspace_id, label_id)
        REFERENCES public.labels (workspace_id, id)
        ON DELETE CASCADE
);

CREATE INDEX ix_task_labels_label_id
    ON public.task_labels (label_id, task_id);


CREATE TABLE public.task_comments (
    id uuid PRIMARY KEY,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    author_user_id uuid NOT NULL,
    body text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_task_comments_task
        FOREIGN KEY (workspace_id, task_id)
        REFERENCES public.tasks (workspace_id, id)
        ON DELETE CASCADE,
    CONSTRAINT fk_task_comments_author
        FOREIGN KEY (author_user_id)
        REFERENCES public.users (id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_task_comments_body_not_empty
        CHECK (btrim(body) <> '')
);

CREATE INDEX ix_task_comments_task_created
    ON public.task_comments (task_id, created_at, id);


CREATE TABLE public.task_activity_logs (
    id uuid PRIMARY KEY,
    workspace_id uuid NOT NULL,
    task_id uuid NOT NULL,
    actor_user_id uuid NOT NULL,
    event_type varchar(50) NOT NULL,
    details jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_task_activity_logs_task
        FOREIGN KEY (workspace_id, task_id)
        REFERENCES public.tasks (workspace_id, id)
        ON DELETE CASCADE,
    CONSTRAINT fk_task_activity_logs_actor
        FOREIGN KEY (actor_user_id)
        REFERENCES public.users (id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_task_activity_logs_event_type_not_empty
        CHECK (btrim(event_type) <> ''),
    CONSTRAINT ck_task_activity_logs_details_object
        CHECK (jsonb_typeof(details) = 'object')
);

CREATE INDEX ix_task_activity_logs_task_created
    ON public.task_activity_logs (task_id, created_at, id);


CREATE FUNCTION public.set_task_update_metadata()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.row_version := OLD.row_version + 1;
    NEW.updated_at := CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$;

CREATE TRIGGER set_task_update_metadata
BEFORE UPDATE ON public.tasks
FOR EACH ROW
EXECUTE FUNCTION public.set_task_update_metadata();
