# TempoDesk Sprint 0 Tickets — Foundation and Developer Experience

**Sprint:** 0  
**Duration:** 2 weeks  
**Sprint goal:** Establish a reproducible development foundation in which the React client, ASP.NET Core API, Worker, and PostgreSQL can be configured, built, tested, and run locally and in CI.

> **Jira key note:** `SCRUM-5` is the actual key assigned by Jira to the first ticket. Keys `SCRUM-6` through `SCRUM-18` assume the remaining tickets are created in this exact order with no other issue created in the project between them. Jira is the source of truth; update this document if Jira assigns a different sequence.

## Sprint outcome

At the end of Sprint 0, a new developer must be able to clone the repository, configure local environment variables, start the required services, run the quality gates, and verify API/database health by following the repository documentation.

## Ticket summary

| ID       | Type  | Title                                                         | Priority | Points | Depends on                   |
| -------- | ----- | ------------------------------------------------------------- | -------- | -----: | ---------------------------- |
| SCRUM-5  | Task  | Establish repository standards and shared .NET build settings | Highest  |      2 | —                            |
| SCRUM-6  | Story | Provision PostgreSQL for local development                    | Highest  |      3 | SCRUM-5                      |
| SCRUM-7  | Story | Implement the database-first migration workflow               | Highest  |      5 | SCRUM-6                      |
| SCRUM-8  | Story | Add strongly typed configuration and startup validation       | High     |      3 | SCRUM-5                      |
| SCRUM-9  | Story | Standardize API errors with Problem Details                   | High     |      3 | SCRUM-8                      |
| SCRUM-10 | Story | Add structured request logging and trace correlation          | High     |      3 | SCRUM-8                      |
| SCRUM-11 | Story | Add API and Worker health checks                              | High     |      3 | SCRUM-6, SCRUM-8             |
| SCRUM-12 | Story | Establish the React application foundation                    | Highest  |      5 | SCRUM-5                      |
| SCRUM-13 | Story | Implement the frontend API client and error handling          | High     |      3 | SCRUM-9, SCRUM-12            |
| SCRUM-14 | Story | Create the full local Docker Compose environment              | Highest  |      5 | SCRUM-6, SCRUM-11, SCRUM-12  |
| SCRUM-15 | Task  | Add architecture boundary tests                               | High     |      3 | SCRUM-5                      |
| SCRUM-16 | Story | Create the continuous integration pipeline                    | Highest  |      5 | SCRUM-7, SCRUM-12, SCRUM-15  |
| SCRUM-17 | Task  | Add foundation smoke and integration tests                    | High     |      3 | SCRUM-7, SCRUM-11, SCRUM-13  |
| SCRUM-18 | Task  | Write the developer setup and troubleshooting guide           | High     |      3 | SCRUM-14, SCRUM-16, SCRUM-17 |

**Planned total:** 49 story points. This is a reference estimate; the team should re-estimate before sprint commitment.

---

## SCRUM-5 — Establish repository standards and shared .NET build settings

**Type:** Task  
**Priority:** Highest  
**Estimate:** 2 points  
**Owner:** Backend/Platform  
**Dependencies:** None

### Description

Define shared repository conventions so all .NET projects compile with consistent language, nullability, analyzer, warning, and artifact settings. Remove generated build output and local secrets from version control.

### Implementation scope

- Add root `.gitignore` rules for .NET, Node.js, IDE, test, coverage, environment, and generated artifacts.
- Add `server/Directory.Build.props` with common .NET settings.
- Enable nullable reference types and implicit usings for all server projects.
- Enable current .NET analyzers and treat analyzer warnings consistently.
- Define `artifacts/` as the shared build/test output location where appropriate.
- Document project dependency direction in `server/README.md`.
- Confirm the solution contains six production projects and four test projects.

### Acceptance criteria

1. `dotnet build server/TempoDesk.sln` succeeds from the repository root.
2. No `bin`, `obj`, `node_modules`, secret, or local environment file is tracked.
3. Domain has no project reference to Application, Infrastructure, API, Worker, or DocumentProcessing.
4. Application references Domain only.
5. Infrastructure references Application and Domain.
6. API references Application and Infrastructure.
7. Worker references Application, Infrastructure, and DocumentProcessing.
8. The dependency rules are documented in `server/README.md`.

### Verification

```bash
dotnet build server/TempoDesk.sln
dotnet sln server/TempoDesk.sln list
git status --short
```

---

## SCRUM-6 — Provision PostgreSQL for local development

**Type:** Story  
**Priority:** Highest  
**Estimate:** 3 points  
**Owner:** Backend/Platform  
**Dependencies:** SCRUM-5

### Description

Provide a reproducible PostgreSQL service for local development with explicit database, migration-role, and runtime-role configuration.

### Implementation scope

- Add a pinned PostgreSQL container image to `docker-compose.yml`.
- Configure a persistent named volume.
- Add a PostgreSQL health check.
- Add `.env.example` variables for host, port, database, migration user, runtime user, and passwords.
- Add bootstrap SQL that creates separate migration and runtime roles.
- Grant schema modification rights only to the migration role.
- Grant the runtime role only the privileges required by the application.
- Do not commit real passwords.

### Acceptance criteria

1. `docker compose up -d postgres` starts PostgreSQL successfully.
2. The PostgreSQL container becomes healthy.
3. Data survives a normal container restart.
4. The migration role can create and alter schema objects.
5. The runtime role cannot create or drop schema objects.
6. Local connection values are documented in `.env.example` without real secrets.

### Verification

```bash
docker compose up -d postgres
docker compose ps postgres
docker compose exec postgres pg_isready
```

---

## SCRUM-7 — Implement the database-first migration workflow

**Type:** Story  
**Priority:** Highest  
**Estimate:** 5 points  
**Owner:** Backend  
**Dependencies:** SCRUM-6

### Description

Create a deterministic SQL-first workflow in which schema migrations are authored and applied before EF Core models are scaffolded into Infrastructure.

### Implementation scope

- Define migration naming as `YYYYMMDDHHMM_<description>.sql`.
- Add `database/migrations`, `database/functions`, `database/views`, and `database/seeds` documentation.
- Add an initial migration that creates a schema-history table owned by the migration role.
- Add a migration runner script or .NET migration utility that applies pending files in lexical order.
- Record file name, checksum, applied timestamp, and execution duration.
- Refuse to run when an already-applied migration checksum changes.
- Add a documented EF Core scaffold command targeting `server/src/TempoDesk.Infrastructure/Persistence/Generated`.
- Ensure generated files are replaceable and contain no handwritten business logic.

### Acceptance criteria

1. Running migrations on an empty database succeeds.
2. Running migrations a second time performs no schema changes.
3. Editing an applied migration causes a checksum failure.
4. A failed migration does not get recorded as successful.
5. The scaffold command produces `TempoDeskDbContext` and entities only under `Persistence/Generated`.
6. The workflow is executable locally and in CI.

### Verification

```bash
docker compose up -d postgres
dotnet run --project server/tools/TempoDesk.DatabaseMigrator
dotnet run --project server/tools/TempoDesk.DatabaseMigrator
```

---

## SCRUM-8 — Add strongly typed configuration and startup validation

**Type:** Story  
**Priority:** High  
**Estimate:** 3 points  
**Owner:** Backend  
**Dependencies:** SCRUM-5

### Description

Replace direct configuration-string access with typed options that fail fast when required settings are missing or invalid.

### Implementation scope

- Add typed options for database, authentication, CORS, Jira placeholders, email, file storage, and background jobs.
- Bind options through extension methods in the appropriate composition root.
- Validate required fields, URLs, durations, and numeric ranges on startup.
- Keep secrets out of committed `appsettings` files.
- Support environment-variable overrides.
- Add sanitized startup logs that state which integrations are enabled without logging credentials.

### Acceptance criteria

1. API startup fails with a clear message when a required setting is absent.
2. Worker startup fails with a clear message when its required setting is invalid.
3. Environment variables override non-secret development defaults.
4. No secret value is printed in logs or exception messages.
5. Configuration validation has automated tests.

### Verification

```bash
dotnet test server/tests/TempoDesk.Application.Tests --filter Configuration
dotnet run --project server/src/TempoDesk.Api
```

---

## SCRUM-9 — Standardize API errors with Problem Details

**Type:** Story  
**Priority:** High  
**Estimate:** 3 points  
**Owner:** Backend  
**Dependencies:** SCRUM-8

### Description

Provide a consistent RFC-compatible error contract so the React client can handle validation, authorization, conflict, not-found, and unexpected failures predictably.

### Implementation scope

- Add centralized exception-handling middleware.
- Map validation failures to HTTP 400.
- Map authentication failures to HTTP 401.
- Map authorization failures to HTTP 403.
- Map missing resources to HTTP 404.
- Map optimistic concurrency conflicts to HTTP 409.
- Map unexpected errors to HTTP 500 without leaking stack traces.
- Include stable error type, title, status, detail, and `traceId`.
- Add a development-only endpoint or test controller for contract verification; do not expose it in production.

### Acceptance criteria

1. All API errors use `application/problem+json`.
2. Every error response includes `traceId`.
3. Validation errors identify invalid fields.
4. Production HTTP 500 responses contain no stack trace, connection string, token, or SQL text.
5. API tests cover every supported status mapping.

### Verification

```bash
dotnet test server/tests/TempoDesk.Api.Tests --filter ProblemDetails
```

---

## SCRUM-10 — Add structured request logging and trace correlation

**Type:** Story  
**Priority:** High  
**Estimate:** 3 points  
**Owner:** Backend/Platform  
**Dependencies:** SCRUM-8

### Description

Add structured logs that correlate API requests, Worker jobs, and future external integration calls without exposing sensitive data.

### Implementation scope

- Add request logging middleware.
- Use W3C trace context and propagate the current trace identifier.
- Add request method, path template, status code, duration, and trace ID to structured fields.
- Define placeholders for user ID, workspace ID, and job ID when they become available.
- Add sensitive-header and sensitive-body redaction rules.
- Exclude health endpoint noise from normal information-level request logs.
- Define log-level overrides for framework categories.

### Acceptance criteria

1. Every normal API request produces one completion log with a trace ID.
2. Problem Details and logs share the same trace ID.
3. Authorization headers, cookies, passwords, OAuth codes, and refresh tokens are never logged.
4. Request duration is recorded as a numeric field.
5. Logging behavior is covered by automated tests or a test sink.

### Verification

```bash
dotnet test server/tests/TempoDesk.Api.Tests --filter Logging
```

---

## SCRUM-11 — Add API and Worker health checks

**Type:** Story  
**Priority:** High  
**Estimate:** 3 points  
**Owner:** Backend/Platform  
**Dependencies:** SCRUM-6, SCRUM-8

### Description

Expose liveness and readiness signals that distinguish a running process from a process ready to serve traffic.

### Implementation scope

- Add `/health/live` for process liveness.
- Add `/health/ready` for PostgreSQL and required service readiness.
- Return machine-readable JSON with status and elapsed time.
- Keep secret configuration out of health responses.
- Add a Worker heartbeat or readiness mechanism suitable for Docker health checks.
- Configure Docker Compose health checks to use these endpoints/signals.

### Acceptance criteria

1. `/health/live` returns success when the API process is running.
2. `/health/ready` returns failure when PostgreSQL is unavailable.
3. `/health/ready` returns success after PostgreSQL recovers.
4. Health responses expose no credentials or connection strings.
5. Docker Compose reports API, Worker, and PostgreSQL health correctly.

### Verification

```bash
curl --fail http://localhost:5023/health/live
curl --fail http://localhost:5023/health/ready
docker compose ps
```

---

## SCRUM-12 — Establish the React application foundation

**Type:** Story  
**Priority:** Highest  
**Estimate:** 5 points  
**Owner:** Frontend  
**Dependencies:** SCRUM-5

### Description

Create a maintainable React application shell with routing, feature boundaries, consistent linting, environment handling, and baseline accessibility.

### Implementation scope

- Confirm Node.js 22.12 or newer.
- Configure React Router.
- Add the application shell, top-level error boundary, and not-found route.
- Create feature directories for auth, workspaces, projects, tasks, boards, calendar, time tracking, Jira, reports, and exports.
- Configure TypeScript strict mode.
- Configure ESLint and formatting rules.
- Add environment typing for API and SignalR base URLs.
- Add a development landing page showing client build information without secrets.
- Add Vitest and React Testing Library.

### Acceptance criteria

1. `npm --prefix client/web run lint` succeeds.
2. `npm --prefix client/web run build` succeeds.
3. Unknown routes render a not-found page.
4. An unhandled component error renders the application error boundary.
5. TypeScript strict mode is enabled.
6. Client environment variables are typed and validated.

### Verification

```bash
node --version
npm --prefix client/web run lint
npm --prefix client/web run test
npm --prefix client/web run build
```

---

## SCRUM-13 — Implement the frontend API client and error handling

**Type:** Story  
**Priority:** High  
**Estimate:** 3 points  
**Owner:** Frontend  
**Dependencies:** SCRUM-9, SCRUM-12

### Description

Create one typed HTTP boundary that parses TempoDesk responses, normalizes Problem Details, supports cancellation, and prevents feature code from calling `fetch` directly.

### Implementation scope

- Create a typed API client with base URL configuration.
- Support JSON request/response and empty successful responses.
- Parse `application/problem+json` into a stable client error type.
- Preserve HTTP status, trace ID, field errors, and safe detail.
- Support `AbortSignal` for request cancellation.
- Add request timeout behavior.
- Add a development health query displayed on the landing page.
- Reserve token-injection hooks without implementing authentication in Sprint 0.

### Acceptance criteria

1. Feature modules do not call global `fetch` directly.
2. Problem Details is converted into one typed client error.
3. Field validation errors remain available to forms.
4. Requests can be cancelled and timed out.
5. The development page displays API readiness when the API is available.
6. Unit tests cover success, no-content, validation error, server error, timeout, and cancellation.

### Verification

```bash
npm --prefix client/web run test -- api-client
npm --prefix client/web run build
```

---

## SCRUM-14 — Create the full local Docker Compose environment

**Type:** Story  
**Priority:** Highest  
**Estimate:** 5 points  
**Owner:** Platform/Full-stack  
**Dependencies:** SCRUM-6, SCRUM-11, SCRUM-12

### Description

Provide a single documented command that starts PostgreSQL, API, Worker, and the web client with dependency-aware health checks.

### Implementation scope

- Add development Dockerfiles for API, Worker, and web.
- Add services to `docker-compose.yml`.
- Use explicit service names, networks, ports, volumes, and health checks.
- Start API and Worker only after PostgreSQL is healthy.
- Pass configuration through environment variables.
- Mount source only where required for the chosen development workflow.
- Add graceful shutdown settings.
- Ensure containers do not run as root where practical.

### Acceptance criteria

1. `docker compose up --build` starts all required services.
2. PostgreSQL, API, and Worker become healthy.
3. The web client can reach the API through documented local URLs.
4. Stopping and restarting the stack preserves PostgreSQL data.
5. No secret is embedded in an image or committed Compose file.
6. `docker compose down` stops the stack cleanly.

### Verification

```bash
docker compose up --build -d
docker compose ps
curl --fail http://localhost:5023/health/ready
docker compose down
```

---

## SCRUM-15 — Add architecture boundary tests

**Type:** Task  
**Priority:** High  
**Estimate:** 3 points  
**Owner:** Backend  
**Dependencies:** SCRUM-5

### Description

Protect the modular-monolith dependency direction with executable tests so future features cannot accidentally couple Domain or Application to infrastructure concerns.

### Implementation scope

- Add an architecture-testing library or implement assembly-reference assertions.
- Test that Domain depends on no TempoDesk project.
- Test that Application depends only on Domain.
- Test that Infrastructure does not reference API or Worker.
- Test that DocumentProcessing does not reference API, Infrastructure, or Worker.
- Test that API controllers do not directly depend on the generated EF DbContext.
- Add naming rules for commands, queries, handlers, controllers, and jobs when those types exist.

### Acceptance criteria

1. Tests fail when a forbidden project reference is introduced.
2. Tests run as part of `dotnet test server/TempoDesk.sln`.
3. Rules match the dependency diagram in `server/README.md`.
4. The test project contains no production-only dependency.

### Verification

```bash
dotnet test server/tests/TempoDesk.Domain.Tests --filter Architecture
dotnet test server/TempoDesk.sln --no-build
```

---

## SCRUM-16 — Create the continuous integration pipeline

**Type:** Story  
**Priority:** Highest  
**Estimate:** 5 points  
**Owner:** Platform  
**Dependencies:** SCRUM-7, SCRUM-12, SCRUM-15

### Description

Create a GitHub Actions pull-request pipeline that verifies backend, frontend, database migrations, and dependency health from a clean environment.

### Implementation scope

- Add `.github/workflows/ci.yml`.
- Pin supported .NET 10 and Node.js 22 toolchains.
- Restore and cache NuGet/npm dependencies safely.
- Run backend build and tests.
- Run frontend lint, tests, and production build.
- Start a PostgreSQL service container and validate all migrations.
- Run NuGet vulnerable-package checks and npm audit policy checks.
- Upload test results and coverage artifacts when a job fails.
- Cancel superseded runs on the same pull request.

### Acceptance criteria

1. The pipeline runs on pull requests and the default branch.
2. A backend compile error fails the pipeline.
3. A frontend type/lint/test error fails the pipeline.
4. A broken migration fails the pipeline.
5. A known high/critical dependency vulnerability fails according to the documented policy.
6. No repository secret is required for normal pull-request validation.

### Verification

- Open a pull request containing the workflow.
- Confirm every job completes successfully on the clean branch.
- Confirm an intentionally failing test causes the expected job to fail before merging the ticket.

---

## SCRUM-17 — Add foundation smoke and integration tests

**Type:** Task  
**Priority:** High  
**Estimate:** 3 points  
**Owner:** Full-stack/QA  
**Dependencies:** SCRUM-7, SCRUM-11, SCRUM-13

### Description

Replace empty test assemblies with meaningful foundation tests that prove the API starts, configuration is validated, PostgreSQL is reachable, and the frontend can interpret health responses.

### Implementation scope

- Add an API application factory for integration tests.
- Start PostgreSQL through the selected test-container strategy.
- Test live and ready health endpoints.
- Test readiness failure when the database is unavailable.
- Test the Problem Details contract for an unexpected exception.
- Test the migration runner against an empty database.
- Add frontend tests for health success, loading, unavailable, and malformed response states.

### Acceptance criteria

1. Every test project discovers at least one test.
2. Integration tests use an isolated database.
3. Tests do not depend on a developer's manually running services.
4. Temporary database resources are cleaned after the test run.
5. `dotnet test` and frontend tests pass from a clean checkout.

### Verification

```bash
dotnet test server/TempoDesk.sln
npm --prefix client/web run test
```

---

## SCRUM-18 — Write the developer setup and troubleshooting guide

**Type:** Task  
**Priority:** High  
**Estimate:** 3 points  
**Owner:** Full-stack  
**Dependencies:** SCRUM-14, SCRUM-16, SCRUM-17

### Description

Document the exact path from a clean machine to a running TempoDesk development environment, including common failure recovery.

### Implementation scope

- Add root `README.md` with product and repository overview.
- Document required .NET, Node.js, Docker, and PostgreSQL versions.
- Document `.env` creation from `.env.example`.
- Document native and Docker Compose startup paths.
- Document migration and EF database-first scaffold commands.
- Document all local quality-gate commands.
- Document expected local URLs and health endpoints.
- Add troubleshooting for occupied ports, failed PostgreSQL readiness, NuGet restore, npm engine mismatch, certificate issues, and stale containers/volumes.
- Link the design, roadmap, server structure, and Sprint 0 ticket documents.

### Acceptance criteria

1. A developer unfamiliar with TempoDesk can run the stack using only the guide.
2. Every documented command has been executed successfully on a clean setup or CI-equivalent environment.
3. The guide clearly distinguishes destructive cleanup commands from normal commands.
4. No real secret or user-specific absolute path appears in the documentation.
5. Links to architecture and sprint documents resolve inside the repository.

### Verification

- Follow the README from a clean clone or disposable directory.
- Record and fix every missing prerequisite or undocumented manual step.

---

## Recommended execution order

```text
SCRUM-5
├── SCRUM-6 → SCRUM-7
├── SCRUM-8 → SCRUM-9 → SCRUM-13
│        └── SCRUM-10
│        └── SCRUM-11 ─────────┐
├── SCRUM-12 ──────────────────┼→ SCRUM-14
└── SCRUM-15                   │
                             ├→ SCRUM-17
SCRUM-7 + SCRUM-12 + SCRUM-15 ────┴→ SCRUM-16

SCRUM-14 + SCRUM-16 + SCRUM-17 → SCRUM-18
```

## Sprint review checklist

- Demonstrate the full stack starting from documented commands.
- Demonstrate PostgreSQL health and migration idempotency.
- Demonstrate a successful API health response in the React client.
- Show one standardized Problem Details response with matching trace ID in logs.
- Show architecture tests rejecting a forbidden dependency.
- Show the CI pipeline passing from a clean environment.
- Confirm that committed files contain no credentials or local absolute paths.

## Sprint 0 exit criteria

Sprint 0 is complete only when:

1. All committed tickets meet their acceptance criteria.
2. Local and CI quality gates pass.
3. At least one developer other than the ticket author can follow the setup guide successfully.
4. No high or critical known dependency vulnerability remains unaccepted.
5. The team can begin Sprint 1 Identity work without additional foundation setup.
