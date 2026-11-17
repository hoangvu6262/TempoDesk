# SCRUM-39 Project, Task, and Subtask API Design

**Date:** 2026-11-17  
**Status:** Approved for implementation planning

## Goal

Expose workspace-scoped APIs for projects, tasks, one-level subtasks,
assignees, labels, comments, activity, and My Work. The API must preserve
workspace isolation, enforce the Sprint 3 permission matrix, protect task
updates with optimistic concurrency, and use the SCRUM-38 persistence model
without adding unrelated planning features.

## Scope

This ticket adds:

- Project list, create, read, update, and archive use cases and endpoints.
- Task list, create, read, update, soft-delete, and restore use cases and
  endpoints.
- One subtask level, assignee replacement, label creation/assignment,
  append-only comments, and immutable server-generated activity.
- A timezone-aware My Work query across Personal and Team Workspaces.
- Application-boundary validation, role authorization, safe Problem Details,
  and PostgreSQL integration coverage.

This ticket does not add Jira-style sprints or backlogs, project-specific
membership, private tasks, arbitrary-depth task trees, project restoration,
comment editing/deletion, boards, notifications, dependencies, recurring
tasks, Jira synchronization, frontend UI, or browser acceptance tests.

## Architecture

Follow the existing Application/Infrastructure/API split:

- `TempoDesk.Application` owns `IProjectService`, `ITaskService`, and
  `IMyWorkService`, their request/result models, validation rules, and
  permission vocabulary.
- `TempoDesk.Infrastructure` implements the services directly with
  `TempoDeskIdentityDbContext` and the SCRUM-38 entities. Entity Framework is
  the persistence abstraction; do not add a repository layer.
- `TempoDesk.Api` owns authenticated controllers and transport DTOs. A
  controller extracts the authenticated user ID, validates transport-specific
  input, calls one service, and maps its result to HTTP.

Keep projects, tasks, and My Work as separate services and controllers so the
large task mutation surface does not absorb project administration or the
cross-workspace query. Do not add a mediator framework or another dependency.

## Authorization and Isolation

Extend the existing workspace authorization vocabulary with:

- `ViewWorkItems`: Owner, Admin, Member, and Viewer.
- `ManageProjects`: Owner and Admin.
- `ManageTasks`: Owner, Admin, and Member.

All project and task endpoints first authorize the caller against the route's
workspace and then query by both workspace ID and resource ID. A missing
membership and a guessed ID from another workspace both produce `404`. An
active member whose role cannot perform an action receives `403`.

Project and task access is inherited from the workspace. No project-member
table, private-resource branch, or client-authoritative permission check is
introduced.

## API Contract

Keep the ticket's proposed routes:

- `GET /api/workspaces/{workspaceId}/projects`
- `POST /api/workspaces/{workspaceId}/projects`
- `GET /api/workspaces/{workspaceId}/projects/{projectId}`
- `PATCH /api/workspaces/{workspaceId}/projects/{projectId}`
- `DELETE /api/workspaces/{workspaceId}/projects/{projectId}`
- `GET /api/workspaces/{workspaceId}/projects/{projectId}/tasks`
- `POST /api/workspaces/{workspaceId}/projects/{projectId}/tasks`
- `GET /api/workspaces/{workspaceId}/tasks/{taskId}`
- `PATCH /api/workspaces/{workspaceId}/tasks/{taskId}`
- `DELETE /api/workspaces/{workspaceId}/tasks/{taskId}`
- `POST /api/workspaces/{workspaceId}/tasks/{taskId}/restore`
- `PUT /api/workspaces/{workspaceId}/tasks/{taskId}/assignees`
- `PUT /api/workspaces/{workspaceId}/tasks/{taskId}/labels`
- `GET /api/workspaces/{workspaceId}/tasks/{taskId}/comments`
- `POST /api/workspaces/{workspaceId}/tasks/{taskId}/comments`
- `GET /api/workspaces/{workspaceId}/tasks/{taskId}/activity`
- `GET /api/my-work?timeZone={ianaTimeZone}`

Project list/read operations expose only unarchived projects. Archiving sets
`ArchivedAt`; it does not physically delete the project or its task history.
Tasks belonging to an archived project are excluded from normal reads and My
Work. SCRUM-39 has no project-restore route.

Project and task `PATCH` DTOs are presence-aware: an omitted property remains
unchanged, while an explicit JSON `null` clears a nullable field. This avoids
silently treating "not supplied" and "clear this value" as the same request.
Non-nullable fields reject explicit `null` through Validation Problem Details.

Task list responses remain flat and include `parentTaskId`; clients can render
the single supported nesting level without receiving duplicate nested task
objects. Task detail responses include assignee and label summaries. Comments
and activity use their dedicated chronological endpoints.

`PUT assignees` replaces the complete assignee set with distinct user IDs.
Every assignee must be an active member of the task's workspace.

`PUT labels` replaces the complete label set with distinct display names. The
service trims each name, derives its normalized form, reuses an existing
workspace label case-insensitively, and creates a label only when none exists.
The response returns stable label IDs and display names.

## Task Validation and Lifecycle

Validate mutable task data at the application boundary before saving:

- Title is non-blank and fits the SCRUM-38 300-character limit.
- Status, priority, and source use the closed SCRUM-38 value sets.
- Estimate is absent or positive.
- Due date is not before start date when both exist.
- A parent belongs to the route workspace and project, is active, is not the
  task itself, and is not already a subtask.
- Assignees are distinct active workspace members.
- Label names are non-blank, within the 100-character persistence limit, and
  unique after normalization.
- Comments are non-blank.

The basic workflow permits transitions among `todo`, `in_progress`, and
`done`, including reopening a completed task. Unsupported status values are
rejected. Every actual transition is recorded by the server.

Deleting a task sets its existing `DeletedAt` timestamp; no `isDeleted` column
or migration is added. Deleting a parent soft-deletes its active direct
subtasks in the same transaction with the same timestamp. Restoring the parent
restores only subtasks whose `DeletedAt` equals that parent's deletion
timestamp, so a subtask deleted earlier is not restored accidentally. Normal
reads continue to use the SCRUM-38 task query filter.

## Optimistic Concurrency

Task responses contain `rowVersion` and an `ETag` derived from it. Task update,
delete, and restore require `If-Match: "<rowVersion>"`. Missing or malformed
preconditions produce Validation Problem Details.

The service assigns the submitted version as the original EF concurrency
token and lets PostgreSQL's SCRUM-38 trigger advance the stored version. A
stale write produces HTTP `409 Problem Details` and does not persist partial
changes. After authorization and workspace-safe lookup succeed, the conflict
response may include `currentRowVersion` so the client can reload. It never
reveals a version for an inaccessible resource.

Assignee, label, comment, and field mutations that change a task execute in a
single database transaction with their activity entry. Any failure rolls back
the complete operation.

## Comments and Activity

Comment authorship and activity actors always come from the authenticated
principal. Clients cannot submit actor IDs, event history, or arbitrary
activity payloads. Comments and activity are append-only in Sprint 3.

Use stable server-known activity event types for:

- Task creation.
- Field and status changes.
- Assignee replacement.
- Label replacement.
- Comment addition.
- Task deletion.
- Task restoration.

Each event stores a JSON object containing only the changed server-known
fields or affected resource IDs needed to render history. Do not duplicate
secrets, authentication data, or client-supplied history in activity details.

## My Work and Time Zones

`GET /api/my-work` requires an IANA time zone such as `Asia/Shanghai` or
`America/Los_Angeles`. The frontend can obtain it from
`Intl.DateTimeFormat().resolvedOptions().timeZone`. The server combines the
zone with `TimeProvider.GetUtcNow()` to calculate the caller's local calendar
date. It does not trust a client-supplied local date, infer a zone from language
or IP address, or use a fixed UTC offset that would fail across daylight-saving
changes.

The query includes:

- Every non-completed, non-deleted task in the caller's active Personal
  Workspace, whether assigned or not.
- Every non-completed, non-deleted task assigned to the caller in Team
  Workspaces where the caller is still an active member.
- Recently completed tasks from the same two scopes.

Archived projects are excluded. The current `done` status plus the most recent
server-generated transition to `done` determines completion time. A task
created directly as `done` uses its creation activity time. Old completed tasks
outside the recent window are not considered active My Work.

Return six mutually exclusive groups in this precedence order:

1. `recentlyCompleted`: completed on the caller's local today or previous six
   local dates.
2. `overdue`: incomplete with a due date before local today.
3. `today`: incomplete and due on local today.
4. `upcoming`: incomplete and due during the next seven local dates.
5. `later`: incomplete and due after the upcoming window.
6. `unscheduled`: incomplete with no due date.

The query shape and grouping guarantee that one task appears at most once.
Timestamps remain UTC in persistence and responses; frontend presentation may
format them in the user's locale. `DateOnly` task fields remain calendar dates
and are never timezone-converted.

## Errors and Problem Details

Use the repository's existing Problem Details customization and these status
semantics:

- `400`: invalid names, task fields, dates, estimates, nesting, assignments,
  labels, comments, time zones, or concurrency headers.
- `403`: an active workspace member lacks the required role.
- `404`: the membership or resource is missing, inaccessible, archived, or
  soft-deleted for that operation.
- `409`: the submitted row version is stale or another documented mutation
  conflict occurs.

Validation responses identify fields without exposing internal constraint
names. Database constraints remain a final defense, but expected invalid input
is rejected at the application boundary.

Label creation handles concurrent attempts using the existing unique normalized
name index. On a unique-name race, reload the workspace label and continue with
that record rather than returning a duplicate or leaking a database exception.

## Testing

Follow test-driven development and add the smallest coverage at each boundary:

- Unit tests for the extended workspace permission policy, work-item
  validation, status/nesting behavior, My Work grouping, and controller result
  mapping.
- PostgreSQL integration tests for project/task CRUD, the complete role matrix,
  cross-workspace IDs, active-member assignment, normalized label races,
  immutable activity, transactional rollback, soft-delete/restore cascades,
  and stale row versions.
- My Work tests with a fixed `TimeProvider` for `Asia/Shanghai` and
  `America/Los_Angeles`, including a daylight-saving boundary, all six groups,
  archived/deleted exclusion, Personal versus Team inclusion, and no
  duplicates.

SCRUM-39 does not add browser or end-to-end test infrastructure; SCRUM-41 owns
the sprint-wide automated acceptance and CI flow.

## Operational Safety

This ticket reuses the SCRUM-38 schema and should not require a database
migration. All destructive product actions are soft operations. Multi-record
mutations and their activity logs commit atomically. The final implementation
must build the solution, pass focused and full server tests, and leave tracked
documentation under `/docs`.
