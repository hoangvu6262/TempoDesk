# TempoDesk Sprint 3 Tickets — Projects, Tasks, and My Work

**Sprint:** 3  
**Duration:** 2 weeks  
**Sprint goal:** Let users organize work into projects, manage tasks and
subtasks safely, and see their authorized work in one My Work view.

> **Jira key note:** `SCRUM-38` through `SCRUM-41` are proposed keys based on
> the current repository sequence. Jira is the source of truth; update this
> document if Jira assigns different keys.

## Sprint outcome

At the end of Sprint 3, users can manage projects and tasks in Personal and
Team Workspaces, break tasks into subtasks, assign workspace members, use
labels and comments, and find their own work across accessible workspaces.
Concurrent edits fail explicitly instead of silently overwriting another
user's changes.

## Delivery principles

- Every project and task belongs to exactly one workspace.
- Sprint 3 inherits the existing workspace roles and does not introduce
  project-level membership or private projects.
- Owner and Admin can manage projects. Owner, Admin, and Member can manage
  tasks, subtasks, labels, assignments, and comments. Viewer is read-only.
- Backend authorization remains authoritative; frontend permission checks are
  usability hints only.
- Scope every query and mutation by both workspace and resource identifier.
- Validate that task parents, assignees, and labels belong to the same
  workspace, and that parent and child tasks belong to the same project.
- Use UTC timestamps, database constraints, task soft deletion, and optimistic
  concurrency.
- Generate activity records on the server. Clients cannot submit arbitrary
  activity actors or event history.

## Ticket summary

| ID       | Type  | Title                                             | Priority | Points | Depends on        |
| -------- | ----- | ------------------------------------------------- | -------- | -----: | ----------------- |
| SCRUM-38 | Story | Create the project and task data model            | Highest  |      8 | Sprint 2          |
| SCRUM-39 | Story | Implement project, task, and subtask APIs         | Highest  |     13 | SCRUM-38          |
| SCRUM-40 | Story | Build project, task, and My Work UI               | High     |     13 | SCRUM-39          |
| SCRUM-41 | Task  | Add Sprint 3 automated acceptance coverage and CI | Highest  |      8 | SCRUM-38–SCRUM-40 |

**Planned total:** 42 story points. The four-ticket structure deliberately
keeps the sprint compact; the team should re-estimate before commitment.

---

## SCRUM-38 — Create the project and task data model

**Type:** Story  
**Priority:** Highest  
**Estimate:** 8 points  
**Owner:** Backend/Database  
**Dependencies:** Sprint 2 workspace and authorization schema

### Description

Create the PostgreSQL schema and persistence mappings for projects, tasks,
subtasks, assignments, labels, comments, and task activity. Encode workspace
isolation and lifecycle invariants in the database wherever practical.

### Implementation scope

- Add SQL migrations and Infrastructure mappings for `projects`, `tasks`,
  `task_assignees`, `labels`, `task_labels`, `task_comments`, and
  `task_activity_logs`.
- Do not add `project_members`; all projects inherit workspace access in this
  sprint.
- Store `workspace_id` on projects, tasks, labels, comments, and activity rows
  where it supports explicit isolation and efficient scoped queries.
- Give tasks `project_id`, optional `parent_task_id`, title, description,
  priority, status, optional start and due dates, optional estimated minutes,
  source type, sort key, row version, created/updated timestamps, and optional
  soft-delete timestamp.
- Support stable task statuses `todo`, `in_progress`, and `done`, and
  priorities `none`, `low`, `medium`, `high`, and `urgent`.
- Enforce that a subtask belongs to the same workspace and project as its
  parent and prevent a task from parenting itself.
- Enforce unique assignment and label links per task.
- Require assignees to reference users with active membership in the task's
  workspace at the application boundary.
- Keep labels workspace-scoped so one workspace can reuse a label across its
  projects.
- Add indexes for workspace/project task lists, parent lookup, assignee lookup,
  due-date queries, active records, labels, comments, and task activity.
- Use a monotonically changing row version for optimistic concurrency.
- Define safe foreign-key behavior so deleting a workspace cannot leave
  orphaned project data and deleting a task cannot leave active child records.

### Acceptance criteria

1. A project, task, label, assignment, comment, or activity row cannot reference
   an unrelated or missing workspace resource.
2. A subtask cannot belong to a different workspace or project than its parent.
3. Invalid status and priority values are rejected.
4. Duplicate task assignees and duplicate task-label links are rejected.
5. Soft-deleted tasks remain recoverable and are excluded from active-query
   indexes and default reads.
6. Updating a task changes its row version.
7. The migration is repeatable through the existing database migrator and the
   persistence mappings match the SQL schema.

### Verification

```bash
dotnet run --project server/tools/TempoDesk.DatabaseMigrator
dotnet build server/TempoDesk.sln
```

### Out of scope

- Project-level membership, private projects, or custom project roles.
- Kanban columns, board configuration, and drag-and-drop ordering UI.
- Attachments, recurring tasks, dependencies, and Jira synchronization.

---

## SCRUM-39 — Implement project, task, and subtask APIs

**Type:** Story  
**Priority:** Highest  
**Estimate:** 13 points  
**Owner:** Backend  
**Dependencies:** SCRUM-38

### Description

Expose workspace-scoped APIs for project and task management, including
subtasks, assignments, labels, comments, activity, My Work, soft deletion, and
conflict-safe updates.

### Permission matrix

| Action                                       | Owner | Admin | Member | Viewer |
| -------------------------------------------- | :---: | :---: | :----: | :----: |
| View projects, tasks, comments, and activity |  Yes  |  Yes  |  Yes   |  Yes   |
| Create, rename, or archive a project         |  Yes  |  Yes  |   No   |   No   |
| Create or update a task/subtask              |  Yes  |  Yes  |  Yes   |   No   |
| Delete or restore a task                     |  Yes  |  Yes  |  Yes   |   No   |
| Manage assignees and labels                  |  Yes  |  Yes  |  Yes   |   No   |
| Add a comment                                |  Yes  |  Yes  |  Yes   |   No   |

### Implementation scope

- Add application use cases and endpoints to list, create, read, update, and
  archive projects.
- Add use cases and endpoints to list, create, read, update, soft-delete, and
  restore tasks.
- Create subtasks through the task API and validate parent workspace/project
  consistency and supported nesting.
- Support one subtask level in Sprint 3: a subtask cannot itself have children.
- Add and remove multiple assignees, limited to active members of the task's
  workspace.
- Create and assign workspace labels without creating duplicates caused by
  case-only name differences.
- Add and list task comments; authorship comes only from the authenticated
  principal.
- Generate immutable task activity for creation, field changes, assignment and
  label changes, comments, deletion, and restoration.
- Require the current row version on task updates, deletes, and restores.
- Return HTTP 409 Problem Details for a stale row version and include enough
  information for the client to reload, without exposing another workspace.
- Add a My Work query that returns all active tasks in the caller's Personal
  Workspace plus tasks assigned to the caller in accessible Team Workspaces.
- Group My Work into overdue, today, upcoming seven days, and completed in the
  previous seven days. A task appears in only one group.
- Reuse the Sprint 2 workspace authorization service and return 404 for missing
  and inaccessible resources where distinguishing them would leak data.
- Validate title, dates, estimate, status transitions, parent, assignments, and
  labels at the application boundary.

### Proposed API contract

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
- `GET /api/my-work`

### Acceptance criteria

1. Users can manage projects and tasks in Personal and Team Workspaces according
   to the documented matrix.
2. Guessing an ID from another workspace never returns or mutates its data.
3. A task cannot be assigned to a removed member or linked to another
   workspace's label or parent task.
4. A stale task mutation returns HTTP 409 and does not overwrite newer data.
5. Deleting a task hides it from normal reads; an authorized restore makes it
   active again.
6. Activity records identify the authenticated actor and describe server-known
   changes without accepting client-supplied history.
7. My Work contains only the caller's authorized Personal Workspace tasks and
   assigned Team Workspace tasks, with no duplicate task across groups.
8. Validation, forbidden, missing, and conflict responses use the existing
   Problem Details contract.

### Verification

```bash
dotnet build server/TempoDesk.sln
```

### Out of scope

- Project-specific access control or private tasks.
- Arbitrary-depth task trees.
- Editing or deleting comments.
- Notifications, watchers, task dependencies, and recurring tasks.

---

## SCRUM-40 — Build project, task, and My Work UI

**Type:** Story  
**Priority:** High  
**Estimate:** 13 points  
**Owner:** Frontend  
**Dependencies:** SCRUM-39

### Description

Build the authenticated project and task experience inside the existing app
shell. Keep the active workspace in the route, make common task operations
keyboard accessible, and recover safely from authorization and concurrency
failures.

### Implementation scope

- Add workspace-scoped project and task routes without changing the existing
  workspace switcher or account/session routes.
- Add project navigation with loading, empty, error, and inaccessible states.
- Add a task list and keyboard-accessible task detail side panel.
- Add forms to create and edit tasks with title, description, status, priority,
  start date, due date, estimate, assignees, and labels.
- Add a subtask checklist that supports creating, editing, completing, and
  opening one-level subtasks.
- Add comment composer, comment history, and compact activity history.
- Add My Work navigation and sections for overdue, today, upcoming seven days,
  and recently completed work.
- Keep form state local while a request is pending or the access token is being
  refreshed.
- Send the current row version with task mutations.
- On HTTP 409, preserve the user's draft, explain that the task changed, and
  offer an explicit reload before another save attempt.
- Hide unavailable mutations using effective permissions while still handling
  HTTP 401, 403, and 404 from the backend.
- Provide clear success and failure feedback for all mutations and prevent
  duplicate submissions.
- Follow the existing frontend component, hook, API-client, constant, and
  feature-folder conventions established for SCRUM-36.

### Acceptance criteria

1. Switching workspace updates project and task routes and never renders stale
   data from the previous workspace.
2. Authorized users can create a project, create a task and subtask, assign a
   member, apply a label, change status, and add a comment using the keyboard.
3. Viewer users can inspect project and task information but do not see enabled
   mutation controls.
4. A conflict never silently discards the local draft or overwrites the server
   version.
5. My Work displays only API-authorized tasks in the correct time groups.
6. Loading, empty, 401, 403, 404, 409, validation, and network states provide
   safe and actionable feedback.
7. `/app/account/sessions` and all existing workspace/member flows remain
   reachable.

### Verification

```bash
npm --prefix client/web run format:check
npm --prefix client/web run lint
npm --prefix client/web run build
```

### Out of scope

- Kanban, drag-and-drop, Calendar, time tracking, and dashboards.
- Rich-text editing, attachments, mentions, and realtime collaboration.
- Offline mutation queues or browser persistence of task drafts.

---

## SCRUM-41 — Add Sprint 3 automated acceptance coverage and CI

**Type:** Task  
**Priority:** Highest  
**Estimate:** 8 points  
**Owner:** Full-stack/QA  
**Dependencies:** SCRUM-38, SCRUM-39, SCRUM-40

### Description

Add the smallest automated test infrastructure and coverage needed to prove
Sprint 3 workspace isolation, permissions, data invariants, concurrency safety,
and the critical project-to-task workflow.

### Implementation scope

- Add domain tests for task title, dates, estimates, statuses, subtask depth,
  parent consistency, assignment, and label rules.
- Add database integration tests for foreign keys, workspace isolation, unique
  links, soft deletion, restoration, and row-version conflicts.
- Test every Sprint 3 permission-matrix entry for Owner, Admin, Member, Viewer,
  removed members, and unrelated workspace users.
- Test cross-workspace IDs for projects, tasks, parents, assignees, labels,
  comments, and activity.
- Test simultaneous task updates and prove that exactly one stale writer
  receives a conflict instead of silently overwriting data.
- Test My Work boundaries, time grouping, deduplication, Personal Workspace
  inclusion, Team Workspace assignment, removed membership, and inaccessible
  workspace exclusion.
- Add a critical end-to-end flow: create project → create task → create subtask
  → assign member → add label → comment → update status → verify My Work.
- Add only the frontend test runner and browser automation required by this
  flow; do not introduce broad UI snapshot coverage.
- Run server tests, frontend checks, and the critical end-to-end flow in CI.

### Acceptance criteria

1. Database and application tests prove the core task and subtask invariants.
2. The complete role matrix and cross-workspace isolation are automated.
3. Concurrent task edits cannot pass while silently losing an update.
4. My Work tests prove that only authorized user-relevant tasks are returned.
5. The critical Personal/Team task workflow passes end to end.
6. CI fails on a Sprint 3 regression while existing build, lint, format, and
   dependency gates remain green.

### Verification

```bash
dotnet test server/TempoDesk.sln --configuration Release
npm --prefix client/web run format:check
npm --prefix client/web run lint
npm --prefix client/web run build
```

### Out of scope

- Load, penetration, accessibility-audit, and visual-regression testing.
- Exhaustive browser coverage for every validation message.
- Sprint 4 Kanban and realtime behavior.

---

## Delivery order

```text
SCRUM-38 Data model
  └── SCRUM-39 Backend and authorization
        └── SCRUM-40 Frontend experience
              └── SCRUM-41 Acceptance coverage and CI
```

SCRUM-41 may begin test-fixture and CI preparation after SCRUM-38, but its
critical end-to-end flow depends on the backend and frontend contracts being
stable.
