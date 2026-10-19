# TempoDesk

TempoDesk is being built as a task and time-management platform for individuals and teams. The current increment provides the development foundation: React shell, API/Worker configuration, health checks, SQL migrations, tests and CI.

## Planned product capabilities

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

## Developer setup

### Prerequisites

- Docker with Compose 2.24 or newer, using Linux containers.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for native development and tests; `global.json` selects a stable .NET 10 SDK.
- [Node.js](https://nodejs.org/) 22.12 or newer and npm; CI uses Node 22.
- Bash, curl and tar for the database/stack checks (macOS/Linux, or WSL on Windows).
- PostgreSQL runs in Docker using the pinned `postgres:18.6-bookworm` image; a host PostgreSQL installation is not required.

All commands below start at the repository root unless stated otherwise.

### Run the entire stack

For a new checkout, copy the example once and edit its password placeholders:

```bash
cp .env.example .env
docker compose config --quiet
docker compose up --build -d --wait
docker compose ps --all
```

Keep an existing `.env`; do not overwrite it when updating the repo. `.env` is local
and ignored by Git. Compose waits for PostgreSQL, runs pending migrations, then starts
API, Worker and web. The migrator exiting with code 0 is expected. Other services
should become healthy.

Default local addresses:

- Web: http://localhost:5173 — displays the API readiness state.
- API liveness: http://localhost:5023/health/live.
- API database readiness: http://localhost:5023/health/ready.
- PostgreSQL/DBeaver: `localhost:5432`, database `tempodesk`.
- Worker has no public port; its Docker health check verifies a fresh database heartbeat.

```bash
curl --fail http://localhost:5023/health/live
curl --fail http://localhost:5023/health/ready
docker compose logs --tail 50 api worker
```

Use `tempodesk_runtime` and `RUNTIME_PASSWORD` for API/Worker; use
`tempodesk_migrator` and `MIGRATION_PASSWORD` for schema changes or DBeaver schema
inspection. The application never needs the PostgreSQL superuser password.
Do not publish this development Compose stack directly as a production deployment.

### Native development

Start only the database and migrations:

```bash
docker compose run --build --rm migrator
```

In each terminal that will run API or Worker, set `Database__Password` to the
runtime password from `.env`. For zsh on macOS, this reads it without displaying or
storing it in shell history:

```bash
read -s 'Database__Password?Runtime database password: '
export Database__Password
export Database__Host=localhost Database__Port=5432 Database__Name=tempodesk
```

Adjust the non-secret connection values if `.env` uses another port/database.
Then run API and Worker in separate terminals:

```bash
ASPNETCORE_URLS=http://localhost:5023 ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project server/src/TempoDesk.Api --no-launch-profile
```

```bash
dotnet run --project server/src/TempoDesk.Worker
```

For the web development server, use another terminal:

```bash
npm --prefix client/web ci
npm --prefix client/web run dev
```

Vite proxies `/api` to `http://localhost:5023` and strips the prefix. Set the
server-only `API_PROXY_TARGET` if the API uses another address. Browser defaults
are `/api` and `/hubs`; `VITE_*` values are public build-time configuration, never
secrets. See [web configuration](client/web/README.md).

### Configuration and diagnostics

API and Worker validate typed options before starting. Environment variables use
double underscores, for example `Database__Password`, `Database__Port` and
`BackgroundJobs__HeartbeatIntervalSeconds`. Passwords are required, ports must be
valid and timeouts must be within configured bounds. CORS origins default to
`http://localhost:5173`.

Authentication, Jira, email and file-storage integrations are disabled by default.
Their options are placeholders for later sprints; enabling a flag does not implement
the integration. Enabled integrations require their corresponding settings.
Startup logs show enabled flags, not credentials. API request logs contain the
route template, method, numeric duration, status and W3C trace ID, without headers,
bodies, query strings or raw exception details.

In Development, `/dev/errors/400` (and 401/403/404/409/500) demonstrates the Problem
Details contract. These diagnostic routes are absent in Production. API errors
include `traceId`; a database-unavailable readiness response instead uses the health
JSON contract with HTTP 503. Liveness stays HTTP 200 while the process is running.

## Database-first changes

Write a new SQL file in `database/migrations`, run the migrator, then scaffold the
selected business tables. Applied SQL files are immutable. Generated entities and
`TempoDeskDbContext` belong only in Infrastructure; do not edit generated files or
put credentials in them. See [migration and scaffold instructions](database/README.md)
and [server dependency rules](server/README.md).

## Quality gates and CI

```bash
dotnet build server/TempoDesk.sln
dotnet test server/TempoDesk.sln --no-build
npm --prefix client/web run lint
npm --prefix client/web run test
npm --prefix client/web run build
npm --prefix client/web run format:check
node --test scripts/check-vulnerabilities.test.mjs
bash database/tests/migrator.sh
bash docker/tests/stack.sh
```

Backend build output, test reports and coverage go under root `artifacts/`.
The database script creates an isolated test database and source copy; the stack
script creates its own project, random host ports and disposable volume. Both clean
their own resources without changing the developer database. Integration tests start
their own PostgreSQL container; Docker must be available, but a manually running
database is not required. Stack failures save sanitized logs to `artifacts/stack.log`.

`.github/workflows/ci.yml` runs backend, frontend, database and full-stack checks on
push/PR, cancels superseded runs, and uploads reports/coverage. CI uses test-only
credentials and needs no repository secrets. It audits direct and transitive NuGet
and npm packages: High/Critical advisories fail; Low/Moderate remain in reports for
follow-up. Audit tool/network failures or malformed reports also fail. No advisory
exceptions are configured; any future exception requires a reviewed advisory,
owner, expiry and explicit policy change.

To run the npm policy locally:

```bash
npm --prefix client/web audit --audit-level=high
```

For NuGet, create `artifacts/audit`, save the JSON report, then check its severity:

```bash
mkdir -p artifacts/audit
dotnet list server/TempoDesk.sln package --vulnerable --include-transitive \
  --format json --output-version 1 --no-restore > artifacts/audit/nuget.json
node scripts/check-vulnerabilities.mjs artifacts/audit/nuget.json
```

## Stop and troubleshoot

Normal shutdown preserves database data:

```bash
docker compose down
```

- Occupied ports: change `POSTGRES_PORT`, `API_PORT` or `WEB_PORT` in `.env`, then
  rerun Compose. Update native proxy/connection settings if you use them.
- PostgreSQL not ready: inspect `docker compose logs --tail 100 postgres`. Bootstrap
  runs only on a new volume. Changing `.env` does not change existing role passwords;
  restore the correct local values or deliberately rotate the roles using an admin.
- API/Worker refuses startup: check the named options section in the error and its
  environment variables. Do not paste connection strings, passwords or resolved
  Compose configuration into issue reports.
- Stale containers or code: run `docker compose up --build -d --wait` again. Do not
  remove the volume just to rebuild code. Keep the bootstrap SQL file in its tracked
  location; the bind mount rejects a missing file rather than creating a directory.
- NuGet restore/audit errors: confirm internet/proxy access to nuget.org, the selected
  SDK (`dotnet --info`) and available Docker resources. Retry the failed command;
  do not disable audit or ignore restore failures to make CI green.
- npm engine mismatch: use Node >=22.12 (`node --version`), then `npm ci` with the
  committed lockfile. Do not mix lockfiles from another package manager.
- Local certificate errors: the documented foundation URLs use HTTP. If enabling
  native HTTPS deliberately, trust a development certificate with
  `dotnet dev-certs https --trust`; never disable certificate validation globally.
- ARM/x64 mismatch: build/run use `DOCKER_DEFAULT_PLATFORM` together. Use
  `linux/arm64` for native ARM containers or `linux/amd64` when required by your environment.

**Destructive reset:** `docker compose down --volumes` deletes the local PostgreSQL
volume and its data. Use it only when you explicitly intend to discard that database
and can restore any required data. It is not a normal recovery step.

Planning/design/roadmap files under `/docs/` are local-only and are not included in
a fresh clone. The versioned setup guides above are self-contained; do not stage `/docs/`.

## License

No license has been specified for this project. All rights are reserved unless a license is added later.
