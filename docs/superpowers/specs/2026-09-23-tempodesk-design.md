# TempoDesk MVP Design

**Date:** 2026-09-23  
**Status:** Approved  
**Product:** TempoDesk

## 1. Objective

TempoDesk is a web-based task and time-management product for both personal and team use. A single account owns a private personal workspace and may create or join multiple team workspaces. The MVP combines task management, Kanban, calendar planning, overlapping time tracking, Jira Cloud synchronization, reporting, and Excel export.

The first delivery covers the React web application and ASP.NET Core backend. The Chrome extension and mobile application remain post-MVP clients of the same API.

## 2. MVP Scope

The MVP includes:

- Email/password registration, email verification, password recovery, and Google sign-in.
- Personal and team workspaces with role-based access.
- Projects, tasks, subtasks, assignees, labels, priorities, comments, and activity history.
- Configurable Kanban boards with drag-and-drop ordering.
- Day, week, month, and list calendar views.
- Multiple planned work sessions per task, including overlapping sessions and resize/move interactions.
- Multiple concurrently running timers per user.
- Jira Cloud import and two-way synchronization.
- Dashboards for task, workload, estimate, actual-time, and overdue metrics.
- Filtered asynchronous Excel exports.
- Realtime UI updates for relevant task, timer, and synchronization changes.

The following are explicitly deferred:

- Native mobile application and Chrome extension.
- Full Jira Epic/Sprint parity.
- Billing and subscriptions.
- Redis, microservices, and Kubernetes.
- AI scheduling, internal chat/video, payroll, and formal attendance processing.

## 3. Architecture

TempoDesk uses a modular monolith. It is deployed as a React web client, an ASP.NET Core API process, an ASP.NET Core Worker process, and PostgreSQL. API and Worker share application, domain, and infrastructure libraries. PostgreSQL is the system of record.

Projects:

```text
apps/web

backend/src/
  TempoDesk.Api
  TempoDesk.Application
  TempoDesk.Domain
  TempoDesk.Infrastructure
  TempoDesk.DocumentProcessing
  TempoDesk.Worker

backend/tests/
  TempoDesk.Domain.Tests
  TempoDesk.Application.Tests
  TempoDesk.IntegrationTests
  TempoDesk.Api.Tests

database/
  migrations
  functions
  views
  seeds
```

Responsibilities:

- `TempoDesk.Domain`: business entities, value objects, enums, and domain rules; no infrastructure dependency.
- `TempoDesk.Application`: use cases, commands, queries, DTOs, validation, and integration interfaces.
- `TempoDesk.Infrastructure`: EF Core/PostgreSQL, Identity, Google authentication, Jira client, email, storage, and interface implementations.
- `TempoDesk.DocumentProcessing`: Excel and future document generation behind application interfaces.
- `TempoDesk.Api`: HTTP endpoints, authentication, authorization, middleware, SignalR hubs, and composition root.
- `TempoDesk.Worker`: durable jobs for Jira, reminders, statistics, and exports.

Dependency direction:

```text
Api -> Application
Api -> Infrastructure
Application -> Domain
Infrastructure -> Application + Domain
DocumentProcessing -> Application abstractions
Worker -> Application + Infrastructure + DocumentProcessing
```

REST handles business operations. SignalR distributes task, timer, and synchronization updates. Redis is not part of the MVP. It may later provide distributed caching, locks, shared rate limits, or transient realtime state without becoming the source of record.

## 4. Database-First Strategy

Database migrations are authored first. EF Core scaffolds the current PostgreSQL schema into:

```text
TempoDesk.Infrastructure/Persistence/Generated/
  TempoDeskDbContext.cs
  Entities/
```

Generated files are not edited manually. Domain behavior remains outside generated entities. Partial classes or explicit mappers bridge persistence and domain models. Deployment uses a migration identity; the runtime application identity receives only required privileges.

All workspace-owned records carry `workspace_id`. Foreign keys, constraints, unique indexes, and query indexes are defined in SQL rather than relying only on application validation. Time values are stored in UTC and displayed in the user's configured time zone.

## 5. Core Data Model

### Identity and workspaces

```text
users
external_logins
refresh_tokens
workspaces
workspace_members
workspace_invitations
```

Registration creates one personal workspace. Users may join multiple team workspaces. Initial roles are Owner, Admin, Member, and Viewer. Personal workspace data is never exposed through a team workspace.

### Projects and boards

```text
projects
project_members
boards
board_columns
```

A workspace contains projects; a project contains one or more boards. Columns have stable ordering, optional WIP limits, and optional Jira status mappings. Fractional sort keys avoid rewriting an entire column during ordinary drag-and-drop operations.

### Tasks

```text
tasks
task_assignees
labels
task_labels
task_dependencies
task_comments
task_attachments
task_activity_logs
```

Important task fields include workspace, project, column, optional parent task, source (`manual` or `jira`), title, description, priority, status, start date, due date, estimate, sort key, creator, timestamps, optimistic concurrency version, and soft-delete timestamp. The MVP supports Task to Subtask hierarchy. A task may have multiple assignees.

### Calendar and time tracking

```text
work_sessions
session_assignees
time_entries
timer_events
```

A work session is a planned calendar block with planned start/end, estimate, task, workspace, creator, and status. A task may have multiple sessions, and sessions may overlap. Moving or resizing a session does not change task deadline unless the user explicitly requests it.

Time entries store actual work intervals separately from planned sessions. Start, pause, resume, stop, and manual correction are audited. Multiple timers may run concurrently. Reports expose both summed duration and union duration with overlapping intervals counted once.

### Jira Cloud

```text
jira_connections
jira_project_mappings
jira_status_mappings
jira_user_mappings
jira_issue_mappings
jira_sync_jobs
jira_webhook_events
jira_sync_conflicts
```

OAuth credentials are encrypted at rest. Jira tasks and manual tasks share the `tasks` table. Imported Jira issues synchronize in both directions. Manual tasks remain local until the user explicitly creates a Jira issue. Webhook payloads are durably recorded before asynchronous processing. Idempotency keys prevent duplicate effects.

### Reports, exports, and jobs

```text
daily_task_statistics
daily_member_statistics
report_exports
background_jobs
background_job_attempts
```

PostgreSQL-backed durable jobs use statuses such as pending, processing, completed, failed, and dead-letter. Workers claim jobs with `FOR UPDATE SKIP LOCKED`. Jobs cover Jira synchronization, webhook renewal, reminders, daily aggregation, Excel generation, and cleanup.

## 6. API and Security

The versioned API starts at `/api/v1` and groups endpoints under auth, users, workspaces, projects, boards, tasks, calendar, time entries, Jira, reports, and exports. Errors use RFC-compatible Problem Details and include a trace ID.

Email/password authentication uses ASP.NET Core Identity. Google uses OpenID Connect/OAuth and links to an existing verified account when safe. TempoDesk issues short-lived access tokens and rotating, hashed refresh tokens. Users can revoke individual devices or all sessions.

Every workspace request verifies authentication, membership, and action-level permission. Backend authorization never trusts a client-supplied workspace identifier without membership validation. Sensitive operations are audited. Passwords, tokens, and secrets are excluded from logs. Jira tokens are protected with ASP.NET Core Data Protection for the first deployment and a managed key service when available.

Rate limits apply to authentication, invitations, Jira connection, and export creation. Attachments are validated for size and media type. CORS is restricted to TempoDesk origins.

## 7. Main Data Flows

### Task and Kanban

Task creation validates workspace, project, board column, and assignees in one PostgreSQL transaction, writes an activity record, commits, and broadcasts a SignalR event. Drag-and-drop uses optimistic UI and sends the destination column, sort key, and concurrency version. The frontend rolls back on rejection.

### Calendar

Dragging an unscheduled task onto the calendar creates a work session. Moving and resizing update planned start/end. Backend validation requires a valid positive duration, matching workspace ownership, and valid member assignments.

### Timer

Starting creates an open actual interval; pausing closes it; resuming creates a new interval; stopping finalizes the logical entry. Starting another timer returns information about active timers but does not block the action. SignalR keeps multiple tabs consistent.

### Jira synchronization

App-to-Jira changes commit locally together with a durable sync job. A Worker calls Jira after the user request completes, applies retry with exponential backoff for transient failures, and records final state.

Jira-to-app webhooks are authenticated where supported, persisted quickly, acknowledged, then processed asynchronously. Per-field timestamps/version information permit non-conflicting changes to merge. A simultaneous edit to the same mapped field produces a conflict for user resolution. Sync failure never discards the locally committed edit.

### Reports and Excel

Dashboard endpoints use live queries for current operational metrics and daily aggregates for longer trends. Excel export captures the user's authorized filters, queues a job, and exposes status and authenticated download endpoints. Workbooks contain summary, task detail, time entry, and member-statistics sheets. Limits apply to date range, row count, file size, and retention.

## 8. Web Application

The frontend uses React, TypeScript, and Vite. Recommended supporting libraries are React Router, TanStack Query, Zustand, React Hook Form, Zod, dnd-kit, a mature calendar library, SignalR client, and i18next.

TanStack Query owns server state; Zustand is limited to UI state such as the active workspace, filters, and open panels. Optimistic mutations always define rollback behavior.

Primary navigation:

```text
My Work
Inbox
Workspace
  Overview
  Projects
  Kanban
  Calendar
  Reports
  Members
Jira Integration
Settings
```

`My Work` aggregates authorized assigned work across workspaces without exposing private data to other members. Team calendars support all-members, self-only, and multi-member filters. Personal calendars contain only personal-workspace data. Jira tasks visibly show issue key, external link, and synchronization state.

The MVP is desktop-first, usable on tablet, and supports essential mobile-browser actions such as viewing tasks, quick creation, timers, and list-calendar view.

## 9. Reporting Metrics

Reports support filters for workspace, project, member, date range, source, and task status. The MVP includes:

- Total tasks and completion rate.
- Tasks by status, priority, project, member, and source.
- Overdue tasks and overdue trend.
- Estimated versus actual duration.
- Summed actual duration and non-overlapping actual duration.
- Member workload and completion trend.
- Jira synchronization success, failure, and conflict counts.

The Excel export uses the same filters as the visible dashboard.

## 10. Error Handling and Operations

The API has centralized exception handling and structured logs containing trace, user, workspace, and job identifiers where applicable. Validation and authorization failures are not retried. Jira timeouts, rate limits, and server failures use bounded retry with backoff. Exhausted jobs move to dead-letter status for operator action.

Health checks cover API, PostgreSQL, Worker, and external integration readiness. Operational metrics include API latency/error rate, queue depth, failed jobs, webhook delay, Jira rate limiting, export duration, database saturation, slow queries, and SignalR reconnects.

PostgreSQL backups run daily and restore is tested periodically. Secrets live outside the repository in environment-specific secret storage.

## 11. Testing Strategy

- Domain unit tests cover permissions, time calculations, overlap union, state transitions, and conflict rules.
- Application tests cover commands, queries, validators, mappings, and orchestration.
- Integration tests run against disposable PostgreSQL and cover SQL constraints, transactions, job claiming, Identity, and generated EF mappings.
- API tests verify authentication, authorization, contracts, status codes, and workspace isolation.
- Jira tests use a controlled HTTP stub and recorded contract fixtures; normal CI does not call a live Jira tenant.
- Frontend tests use Vitest and React Testing Library for units/components and Playwright for critical flows.

Critical end-to-end flows include registration, automatic personal workspace creation, team invitation, task creation, Kanban movement, calendar scheduling/resizing, concurrent timers, Jira import/two-way update/conflict, report filtering, and Excel download.

## 12. Delivery and Deployment

Local development uses Docker Compose for web, API, Worker, PostgreSQL, email testing, and optional S3-compatible object storage. Environments are local, staging, and production.

Initial production needs one web deployment, one API instance, one Worker instance, managed PostgreSQL, object storage, HTTPS/reverse proxy, and transactional email. CI builds and type-checks both stacks, executes unit/integration/API tests, validates migrations, and scans dependencies. CD applies backward-compatible migrations before rolling out API, Worker, and web, then runs smoke tests.

## 13. Implementation Sequence

1. Repository foundation, solution structure, Docker Compose, logging, errors, migration workflow, CI, and health checks.
2. Identity, Google sign-in, token rotation, sessions, and automatic personal workspace.
3. Team workspaces, invitations, roles, project membership, and isolation tests.
4. Projects, tasks, subtasks, labels, assignees, comments, activity, and My Work.
5. Boards, columns, Kanban ordering, filters, optimistic UI, and realtime updates.
6. Calendar views, work sessions, move/resize, splitting, overlaps, and member filters.
7. Concurrent timers, audit events, manual corrections, and duration calculations.
8. Jira OAuth, site/project selection, mappings, initial import, and webhook registration.
9. Two-way Jira jobs, retries, idempotency, conflict resolution, and manual-task publishing.
10. Dashboard queries, daily aggregates, filters, and workload/time statistics.
11. Background Excel generation, authorized downloads, limits, and retention.
12. Accessibility, responsive refinement, security review, load testing, UAT, backup/restore rehearsal, and production documentation.

## 14. Architectural Evolution Rules

TempoDesk will not adopt Redis or microservices based on anticipation alone. Redis is introduced only after measured need for cross-instance cache, distributed locks, shared rate limits, or high-volume transient queues. A module becomes a service only when it needs independent scaling or deployment, creates unacceptable resource contention, or has a clearly separate owning team. PostgreSQL remains authoritative throughout these changes.

## 15. Success Criteria

The MVP is successful when one account can manage private and team workspaces, plan tasks through Kanban and calendar sessions, record overlapping actual work, synchronize mapped Jira Cloud issues in both directions without data loss, view authorized team or personal reports, and download an Excel workbook matching the active filters. Workspace isolation, durable job recovery, and critical end-to-end tests must pass before production release.
