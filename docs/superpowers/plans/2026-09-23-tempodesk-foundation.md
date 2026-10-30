# TempoDesk Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the TempoDesk .NET solution, React/TypeScript web application, project references, and baseline build verification.

**Architecture:** A modular monolith contains API, Application, Domain, Infrastructure, DocumentProcessing, and Worker projects. A Vite React SPA lives under `apps/web`; backend tests are separated by test boundary.

**Tech Stack:** .NET 10 LTS, ASP.NET Core, C# 14, xUnit, React, TypeScript, Vite, Node.js 22 LTS.

## Global Constraints

- Use the `TempoDesk` product and namespace prefix everywhere.
- Keep generated EF Core database-first models under `TempoDesk.Infrastructure/Persistence/Generated` in later work.
- PostgreSQL is authoritative; do not introduce Redis or microservices.
- Do not add feature packages until the feature that consumes them is implemented.
- Use Node.js 22.12 or newer for the Vite toolchain.

---

### Task 1: Scaffold the Backend Solution

**Files:**
- Create: `TempoDesk.sln`
- Create: `backend/src/TempoDesk.Api/TempoDesk.Api.csproj`
- Create: `backend/src/TempoDesk.Application/TempoDesk.Application.csproj`
- Create: `backend/src/TempoDesk.Domain/TempoDesk.Domain.csproj`
- Create: `backend/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj`
- Create: `backend/src/TempoDesk.DocumentProcessing/TempoDesk.DocumentProcessing.csproj`
- Create: `backend/src/TempoDesk.Worker/TempoDesk.Worker.csproj`
- Create: `backend/tests/TempoDesk.Domain.Tests/TempoDesk.Domain.Tests.csproj`
- Create: `backend/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj`
- Create: `backend/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj`
- Create: `backend/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj`

**Interfaces:**
- Consumes: .NET 10 SDK templates.
- Produces: A compilable solution whose project names establish the architectural boundaries.

- [ ] **Step 1: Verify the SDK**

Run:

```bash
dotnet --version
```

Expected: version `10.0.100` or newer.

- [ ] **Step 2: Create the solution and production projects**

Run from the empty `TempoDesk` repository root:

```bash
dotnet new sln -n TempoDesk --format sln
mkdir -p backend/src backend/tests apps
dotnet new webapi -n TempoDesk.Api -o backend/src/TempoDesk.Api --framework net10.0 --use-controllers
dotnet new classlib -n TempoDesk.Application -o backend/src/TempoDesk.Application --framework net10.0
dotnet new classlib -n TempoDesk.Domain -o backend/src/TempoDesk.Domain --framework net10.0
dotnet new classlib -n TempoDesk.Infrastructure -o backend/src/TempoDesk.Infrastructure --framework net10.0
dotnet new classlib -n TempoDesk.DocumentProcessing -o backend/src/TempoDesk.DocumentProcessing --framework net10.0
dotnet new worker -n TempoDesk.Worker -o backend/src/TempoDesk.Worker --framework net10.0
```

Expected: six production projects are created without template errors.

- [ ] **Step 3: Create the test projects**

Run:

```bash
dotnet new xunit -n TempoDesk.Domain.Tests -o backend/tests/TempoDesk.Domain.Tests --framework net10.0
dotnet new xunit -n TempoDesk.Application.Tests -o backend/tests/TempoDesk.Application.Tests --framework net10.0
dotnet new xunit -n TempoDesk.IntegrationTests -o backend/tests/TempoDesk.IntegrationTests --framework net10.0
dotnet new xunit -n TempoDesk.Api.Tests -o backend/tests/TempoDesk.Api.Tests --framework net10.0
```

Expected: four xUnit projects are created.

- [ ] **Step 4: Add all projects to the solution**

Run:

```bash
dotnet sln TempoDesk.sln add backend/src/TempoDesk.Api/TempoDesk.Api.csproj
dotnet sln TempoDesk.sln add backend/src/TempoDesk.Application/TempoDesk.Application.csproj
dotnet sln TempoDesk.sln add backend/src/TempoDesk.Domain/TempoDesk.Domain.csproj
dotnet sln TempoDesk.sln add backend/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj
dotnet sln TempoDesk.sln add backend/src/TempoDesk.DocumentProcessing/TempoDesk.DocumentProcessing.csproj
dotnet sln TempoDesk.sln add backend/src/TempoDesk.Worker/TempoDesk.Worker.csproj
dotnet sln TempoDesk.sln add backend/tests/TempoDesk.Domain.Tests/TempoDesk.Domain.Tests.csproj
dotnet sln TempoDesk.sln add backend/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj
dotnet sln TempoDesk.sln add backend/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj
dotnet sln TempoDesk.sln add backend/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj
```

Expected: `dotnet sln TempoDesk.sln list` reports ten projects.

### Task 2: Establish Project Dependencies

**Files:**
- Modify: `backend/src/TempoDesk.Application/TempoDesk.Application.csproj`
- Modify: `backend/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj`
- Modify: `backend/src/TempoDesk.DocumentProcessing/TempoDesk.DocumentProcessing.csproj`
- Modify: `backend/src/TempoDesk.Api/TempoDesk.Api.csproj`
- Modify: `backend/src/TempoDesk.Worker/TempoDesk.Worker.csproj`
- Modify: all four test project files.

**Interfaces:**
- Consumes: production and test projects from Task 1.
- Produces: One-way references that enforce the approved dependency direction.

- [ ] **Step 1: Add production references**

Run:

```bash
dotnet add backend/src/TempoDesk.Application/TempoDesk.Application.csproj reference backend/src/TempoDesk.Domain/TempoDesk.Domain.csproj
dotnet add backend/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj reference backend/src/TempoDesk.Application/TempoDesk.Application.csproj backend/src/TempoDesk.Domain/TempoDesk.Domain.csproj
dotnet add backend/src/TempoDesk.DocumentProcessing/TempoDesk.DocumentProcessing.csproj reference backend/src/TempoDesk.Application/TempoDesk.Application.csproj
dotnet add backend/src/TempoDesk.Api/TempoDesk.Api.csproj reference backend/src/TempoDesk.Application/TempoDesk.Application.csproj backend/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj
dotnet add backend/src/TempoDesk.Worker/TempoDesk.Worker.csproj reference backend/src/TempoDesk.Application/TempoDesk.Application.csproj backend/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj backend/src/TempoDesk.DocumentProcessing/TempoDesk.DocumentProcessing.csproj
```

Expected: each command reports that the references were added.

- [ ] **Step 2: Add test references**

Run:

```bash
dotnet add backend/tests/TempoDesk.Domain.Tests/TempoDesk.Domain.Tests.csproj reference backend/src/TempoDesk.Domain/TempoDesk.Domain.csproj
dotnet add backend/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj reference backend/src/TempoDesk.Application/TempoDesk.Application.csproj backend/src/TempoDesk.Domain/TempoDesk.Domain.csproj
dotnet add backend/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj reference backend/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj backend/src/TempoDesk.Application/TempoDesk.Application.csproj
dotnet add backend/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj reference backend/src/TempoDesk.Api/TempoDesk.Api.csproj
```

Expected: test projects reference only the layers they test.

- [ ] **Step 3: Build and test the backend**

Run:

```bash
dotnet build TempoDesk.sln
dotnet test TempoDesk.sln --no-build
```

Expected: build succeeds with zero errors and all template tests pass.

- [ ] **Step 4: Commit the backend foundation**

```bash
git add TempoDesk.sln backend
git commit -m "chore: scaffold TempoDesk backend"
```

### Task 3: Scaffold React and TypeScript

**Files:**
- Create: `apps/web/package.json`
- Create: `apps/web/src/main.tsx`
- Create: `apps/web/src/App.tsx`
- Create: `apps/web/vite.config.ts`
- Create: `apps/web/tsconfig.json`

**Interfaces:**
- Consumes: Node.js 22.12 or newer and npm.
- Produces: A buildable React/TypeScript SPA under `apps/web`.

- [ ] **Step 1: Select Node.js 22 LTS**

If `nvm` is installed, run:

```bash
nvm install 22
nvm use 22
node --version
```

Expected: `v22.12.0` or newer. If `nvm` is unavailable, install Node.js 22 LTS using the machine's normal package manager before continuing.

- [ ] **Step 2: Create the Vite application**

Run:

```bash
npm create vite@latest apps/web -- --template react-ts
npm --prefix apps/web install
```

Expected: Vite creates a React/TypeScript application and npm completes without engine errors.

- [ ] **Step 3: Verify frontend quality gates**

Run:

```bash
npm --prefix apps/web run lint
npm --prefix apps/web run build
```

Expected: lint and production build succeed.

- [ ] **Step 4: Commit the web foundation**

```bash
git add apps/web
git commit -m "chore: scaffold TempoDesk web app"
```

### Task 4: Verify the Repository Foundation

**Files:**
- Verify: `TempoDesk.sln`
- Verify: `backend/`
- Verify: `apps/web/`

**Interfaces:**
- Consumes: completed backend and frontend scaffolds.
- Produces: A clean, reproducible starting point for the database and identity plans.

- [ ] **Step 1: Run all current checks**

```bash
dotnet build TempoDesk.sln
dotnet test TempoDesk.sln --no-build
npm --prefix apps/web run lint
npm --prefix apps/web run build
```

Expected: every command exits with status zero.

- [ ] **Step 2: Inspect dependency direction**

```bash
dotnet list backend/src/TempoDesk.Application/TempoDesk.Application.csproj reference
dotnet list backend/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj reference
dotnet list backend/src/TempoDesk.Api/TempoDesk.Api.csproj reference
dotnet list backend/src/TempoDesk.Worker/TempoDesk.Worker.csproj reference
```

Expected: Domain has no project reference; Application references Domain; Infrastructure references Application and Domain; API references Application and Infrastructure; Worker references Application, Infrastructure, and DocumentProcessing.

- [ ] **Step 3: Confirm a clean working tree**

```bash
git status --short
```

Expected: no output after the two commits.
