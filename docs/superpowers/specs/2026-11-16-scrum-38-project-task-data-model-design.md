# SCRUM-38 Project and Task Data Model Design

**Date:** 2026-11-16  
**Status:** Approved for implementation planning

## Goal

Create the PostgreSQL schema and Infrastructure persistence mappings for
projects, tasks, subtasks, assignees, labels, comments, and task activity. The
database must preserve workspace isolation, task lifecycle rules, and
optimistic concurrency while remaining a focused foundation for SCRUM-39.

## Scope

This ticket adds:

- A versioned SQL migration for `projects`, `tasks`, `task_assignees`,
  `labels`, `task_labels`, `task_comments`, and `task_activity_logs`.
- Infrastructure entities and Entity Framework Core mappings that match the
  SQL schema.
- PostgreSQL integration tests for the SCRUM-38 acceptance criteria.
- Test database reset support for the new tables.

This ticket does not add APIs, application use cases, UI, project membership,
boards, attachments, recurring tasks, dependencies, or Jira synchronization.
Projects inherit workspace access.

## Approach

Follow the repository's current SQL-first, manually mapped persistence pattern.
The SQL migration owns foreign keys, checks, unique constraints, indexes, and a
small task-version trigger. Infrastructure exposes matching entities and model
configuration through the existing `TempoDeskIdentityDbContext`.

This avoids introducing generated scaffold output into a context that currently
uses explicit mappings. It also avoids a trigger-heavy design: stable relational
invariants belong in PostgreSQL, while actor permissions and active workspace
membership remain application-boundary checks in SCRUM-39.

## Schema

### Projects

`projects` contains:

- `id`
- `workspace_id`
- `name`
- optional `description`
- `created_at`
- `updated_at`
- optional `archived_at`

Project archival is soft. Normal project reads in SCRUM-39 will exclude archived
projects while preserving their tasks and history. A composite unique key on
`(workspace_id, id)` supports workspace-safe foreign keys from tasks.

### Tasks

`tasks` contains:

- `id`
- `workspace_id`
- `project_id`
- optional `parent_task_id`
- `created_by_user_id`
- `title`
- optional `description`
- `priority`: `none`, `low`, `medium`, `high`, or `urgent`
- `status`: `todo`, `in_progress`, or `done`
- optional `start_date` and `due_date`, stored as PostgreSQL `date`
- optional positive `estimated_minutes`
- `source_type`: `manual` or `jira`
- fractional `sort_key`
- positive, monotonically increasing `row_version`
- `created_at`
- `updated_at`
- optional `deleted_at`

Dates deliberately have no time component. Time-specific scheduling belongs to
the later calendar sprint. A composite foreign key from
`(workspace_id, project_id)` to the project guarantees workspace consistency.
A composite self-reference containing workspace and project guarantees that a
subtask has the same workspace and project as its parent. A check constraint
prevents a task from parenting itself.

The database does not enforce the one-subtask-level product rule. SCRUM-39 will
validate that rule at the application boundary, where it can return the API's
documented validation response without adding recursive database triggers.

### Assignments and labels

`task_assignees` stores task, workspace, user, and creation time. Its composite
task foreign key prevents a task/workspace mismatch, and its task/user key
prevents duplicate assignment. The user must exist. SCRUM-39 will additionally
require an active membership in the task's workspace, as specified by the
ticket, because active membership is represented by a partial workspace-member
index and is not a stable foreign-key target.

`labels` stores workspace, display `name`, `normalized_name`, and creation time.
A unique index on `(workspace_id, normalized_name)` prevents case-only
duplicates without requiring the PostgreSQL `citext` extension.

`task_labels` stores task, label, and workspace. Composite foreign keys ensure
both resources belong to that workspace, and its task/label key prevents
duplicate links.

### Comments and activity

`task_comments` stores workspace, task, author, body, and creation time.
Comments are append-only in Sprint 3.

`task_activity_logs` stores workspace, task, actor, event type, a `jsonb`
details payload, and creation time. Activity is immutable. The event type and
payload are written only by server-side use cases in SCRUM-39.

Composite task foreign keys keep comments and activity in the task's workspace.
Author and actor foreign keys ensure referenced users exist.

## Constraints and Lifecycle

PostgreSQL enforces:

- Non-blank project names, task titles, label names, comments, and activity
  event types.
- Allowed task status, priority, and source values.
- Positive estimates and row versions.
- `due_date >= start_date` when both dates exist.
- Same-workspace project, parent task, label link, comment, and activity
  relationships.
- No self-parenting.
- Unique task assignees and task-label links.
- Case-insensitive label uniqueness through `normalized_name`.

Deleting a workspace cascades through all workspace-owned data. Physical
project or task deletion also cascades to dependent records so it cannot leave
active children or orphaned links. Product flows do not physically delete these
records: projects use `archived_at`, and tasks use `deleted_at`.

Entity Framework applies a global query filter to tasks where `deleted_at` is
null. Restore and audit paths in SCRUM-39 can deliberately use
`IgnoreQueryFilters()`.

## Optimistic Concurrency

A `BEFORE UPDATE` trigger on `tasks` sets `updated_at` and increments
`row_version` from the stored value. This protects updates made through EF or
direct SQL and makes the version monotonically increasing.

The EF mapping marks `row_version` as a concurrency token. SCRUM-39 will include
the current version in task mutations and translate stale updates into HTTP 409.

## Indexes

The migration adds focused indexes for:

- Workspace project lists and active projects.
- Active task lists by workspace/project and sort key.
- Parent-task lookup.
- Active due-date queries.
- Assignee/My Work lookup.
- Workspace label lookup.
- Comment timeline lookup by task.
- Activity timeline lookup by task.

Partial indexes exclude soft-deleted tasks where the query represents normal
active reads. Recovery remains possible through primary and unfiltered keys.

## Infrastructure Mapping

Add a focused persistence namespace for the new entities and one configuration
entry point called from `TempoDeskIdentityDbContext.OnModelCreating`. Add
matching `DbSet` properties to the existing context. Mappings explicitly name
tables, columns, constraints, indexes, relationships, defaults, the task query
filter, and the row-version concurrency token.

Navigation properties are added only where they support these mappings or the
next ticket's persistence queries. Business validation does not enter the
persistence entities.

## Testing

PostgreSQL integration tests will prove that:

- Cross-workspace project, parent, label, comment, and activity references are
  rejected.
- Invalid status, priority, source, dates, and estimates are rejected.
- Duplicate assignees and task-label links are rejected.
- A soft-deleted task remains stored, is hidden by the default EF query, and is
  visible through `IgnoreQueryFilters()`.
- Updating a task increases its row version.
- Two contexts using the same task version produce an EF concurrency conflict
  rather than silently overwriting data.
- The EF mappings create and read a representative project/task graph against
  the migrated PostgreSQL schema.

The shared PostgreSQL fixture will truncate the new tables in dependency order.
Final verification uses the existing database migrator workflow, the focused
integration tests, and a full solution build.

## Rollback and Operational Safety

The migration documents a pre-production rollback order. Once applied to a
shared environment, it remains immutable; corrections use a forward migration.
No migration contains secrets or user data.
