# Remaining Sprint 0 implementation plan

**Goal:** Implement SCRUM-8 through SCRUM-18 without business features.

**Constraints:** Keep modular-monolith references; .NET 10, Node >=22.12, PostgreSQL 18.6. Never stage docs, .env or artifacts. Do not change applied SQL migrations or user data. No push/PR/commit unless requested.

## Independent tasks

- [x] Backend (SCRUM-8/9/10/11/15/17): typed options and sanitized fail-fast validation, Problem Details, W3C structured request logs, API process/database health, Worker database heartbeat, production-hidden diagnostics, meaningful configuration/API/PostgreSQL/architecture tests in all four test assemblies. Own server/src and server/tests.
- [x] Frontend (SCRUM-12/13/17): accessible React Router shell, error boundary/404, typed validated public environment, single cancellable/timeout HTTP client, health landing, Vitest/Testing Library, static web Dockerfile/proxy. Own client/web.
- [x] CI (SCRUM-16): PR/push backend/frontend/migration/stack jobs, Node/.NET toolchains/caches, High/Critical vulnerability gate, reports and cancellation. Own .github/workflows and scripts.
- [x] Root integration (SCRUM-14/18): shared server Dockerfile, Compose runtime configuration and health dependencies, full-stack isolated smoke/recovery/persistence check, native/Docker setup and troubleshooting guide.

## Contracts

- Database options: Host, Port, Name, Username=tempodesk_runtime, Password required, CommandTimeoutSeconds.
- GET /health/live and /health/ready: JSON status Healthy/Unhealthy and elapsedMs numeric; readiness 503 on database failure.
- Development-only /dev/errors/{status}: Problem Details for 400/401/403/404/409/500 with traceId; no diagnostics in Production.
- API `--health-check`: GET localhost:8080/health/ready and exit 0/1.
- Worker `--health-check`: heartbeat file /tmp/tempodesk-worker-health.json, fresh <=30s and database healthy.
- Browser /api proxy strips prefix to API8080, /hubs reserved, web8080 exposed as localhost5173; native API5023.

## Verification

- [x] Review independent diffs against ticket acceptance criteria, resolve findings.
- [x] dotnet build/test solution, each test assembly discovers meaningful tests. Release: 0 warnings/errors, 26 passed, 0 skipped.
- [x] npm ci/lint/test/build and dependency audit policies. 47 tests passed; npm audit found 0 vulnerabilities; NuGet audit found no High/Critical; policy parser tests 4 passed.
- [x] bash database/tests/migrator.sh: SQL and EF scaffold from clean isolated source/database.
- [x] bash docker/tests/stack.sh: clean disposable stack, proxy, health, errors/logs, failure/recovery, persistence and cleanup.
- [x] Verify Docker/quality commands in isolated environments and no sensitive/generated files tracked. Keep docs local-only, so the public README links tracked server/database guides rather than untracked sprint/design files.
- [ ] Remote CI success/failure demonstration and another developer's clean-setup walkthrough remain externally verifiable, not claimed from local tests. No push or PR authorized in this request.
