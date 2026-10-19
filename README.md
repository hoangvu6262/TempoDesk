# TempoDesk

TempoDesk is a task and time-management platform for individuals and teams. It brings project planning, Kanban workflows, calendar scheduling, time tracking, Jira synchronization, reporting, and Excel exports into one workspace.

## Core capabilities

- Personal and team workspaces with role-based access
- Projects, tasks, subtasks, assignees, labels, comments, and activity history
- Configurable Kanban boards with drag-and-drop ordering
- Calendar planning with movable and resizable work sessions
- Concurrent timers and auditable time entries
- Two-way Jira Cloud synchronization
- Workload, progress, estimate, actual-time, and overdue reporting
- Filtered Excel exports
- Real-time updates across connected clients

## Tech stack

- **Web:** React, TypeScript, and Vite
- **Backend:** ASP.NET Core on .NET 10
- **Database:** PostgreSQL
- **Architecture:** Modular monolith with separate API and background Worker processes

## Repository structure

```text
TempoDesk/
├── client/web/       # React web application
├── server/src/       # API, Worker, Domain, Application, and Infrastructure projects
├── server/tests/     # Backend test projects
├── database/         # Database migrations, functions, views, and seed data
└── docker/           # Local container configuration
```

The backend follows this dependency direction:

```text
API ------------------------> Application
 |                                |
 +----------> Infrastructure     v
                              Domain

Infrastructure -------------> Application + Domain
DocumentProcessing ---------> Application
Worker ---------------------> Application + Infrastructure + DocumentProcessing
```

The Domain project has no dependency on the other backend projects. Business rules belong in Domain, use-case orchestration belongs in Application, and external concerns such as persistence and integrations belong in Infrastructure.

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js](https://nodejs.org/) and npm
- Docker with Compose for PostgreSQL and database migrations

### Run the web application

```bash
cd client/web
npm install
npm run dev
```

### Run PostgreSQL and database migrations

Copy `.env.example` to `.env` and replace the password placeholders. Then run
from the repository root:

```bash
docker compose run --build --rm migrator
```

This starts PostgreSQL and applies pending SQL migrations. See
[the database workflow](database/README.md) for subsequent schema changes and checks.

### Run the API

From the repository root:

```bash
dotnet restore server/TempoDesk.sln
dotnet run --project server/src/TempoDesk.Api
```

The HTTP development profile listens on `http://localhost:5023`.

### Run the background Worker

From the repository root:

```bash
dotnet run --project server/src/TempoDesk.Worker
```

## Build, lint, and test

Build and lint the web application:

```bash
cd client/web
npm run build
npm run lint
```

Build and test the backend from the repository root:

```bash
dotnet build server/TempoDesk.sln
dotnet test server/TempoDesk.sln
```

## License

No license has been specified for this project. All rights are reserved unless a license is added later.
