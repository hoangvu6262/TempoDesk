# SCRUM-27 Authentication Rate Limits and Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce approved IP/account fixed-window limits on authentication endpoints and enrich every authentication audit row with safe request context and retention configuration.

**Architecture:** A singleton, `TimeProvider`-driven limiter owns thread-safe process-local partitions; controllers acquire policies after model binding and use one safe 429 response helper. A scoped audit context is populated by request middleware and applied centrally by the Identity DbContext before audit rows are saved.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10, PostgreSQL 18, `TimeProvider`, xUnit.

## Global Constraints

- Register: 5 requests per IP per 15 minutes.
- Login: 30 requests per IP and 5 per account per 5 minutes.
- Forgot password: 20 requests per IP and 3 per account per 15 minutes.
- Refresh: 30 requests per IP per minute.
- Account partitions use lowercase SHA-256 of trimmed normalized email; raw email is never stored in limiter state.
- Denials return HTTP 429 ProblemDetails and integer `Retry-After` of at least one second.
- Denied requests do not invoke authentication services.
- Audit retention defaults to 90 days and accepts 1 through 3650 days.
- Never store password, token/hash, cookie, Authorization/JWT, request body, email, or account partition hash in logs/audits.
- Do not add Redis, dependencies, migrations, audit UI/API, cleanup jobs, CAPTCHA, or non-auth rate limits.

---

### Task 1: Add validated security configuration

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/Configuration/FoundationOptions.cs`
- Modify: `server/src/TempoDesk.Api/appsettings.json`
- Modify: `server/src/TempoDesk.Api/appsettings.Development.json`
- Modify: `.env.example`
- Modify: `docker-compose.yml`
- Modify: `server/tests/TempoDesk.Application.Tests/ConfigurationTests.cs`

**Interfaces:**
- Produces: `SecurityOptions` and `AuthenticationRateLimitOptions`.
- Produces exact properties consumed by Task 2: `Register`, `LoginIp`, `LoginAccount`, `ForgotPasswordIp`, `ForgotPasswordAccount`, `Refresh`, and `AuditRetentionDays`.

- [ ] **Step 1: Write failing configuration tests**

Add valid assertions for approved permits/windows and retention. Add invalid
cases for zero permits, zero window seconds, retention zero, and retention 3651.

```csharp
Assert.Equal(5, options.Register.PermitLimit);
Assert.Equal(900, options.Register.WindowSeconds);
Assert.Equal(90, options.AuditRetentionDays);
```

- [ ] **Step 2: Run configuration tests to verify RED**

```bash
dotnet test server/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj --filter FullyQualifiedName~ConfigurationTests
```

Expected: compilation fails because `SecurityOptions` does not exist.

- [ ] **Step 3: Add options and startup validation**

Add `AuthenticationRateLimitOptions` with `PermitLimit` and `WindowSeconds`.
Add `SecurityOptions` defaults: Register 5/900; LoginIp 30/300;
LoginAccount 5/300; ForgotPasswordIp 20/900; ForgotPasswordAccount 3/900;
Refresh 30/60; AuditRetentionDays 90.

Bind section `Security`; validate each permit/window is positive, every window
is at most 86,400 seconds, and retention is 1..3650. Add approved values to
appsettings, Docker environment, and `.env.example`.

- [ ] **Step 4: Run configuration tests to verify GREEN**

Run the Step 2 command.

Expected: all configuration tests pass.

- [ ] **Step 5: Commit configuration**

```bash
git add server/src/TempoDesk.Infrastructure/Configuration/FoundationOptions.cs server/src/TempoDesk.Api/appsettings.json server/src/TempoDesk.Api/appsettings.Development.json .env.example docker-compose.yml server/tests/TempoDesk.Application.Tests/ConfigurationTests.cs
git commit -m "feat(security): configure authentication limits"
```

---

### Task 2: Implement the fixed-window limiter

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/RateLimit/AuthenticationRateLimitPolicy.cs`
- Create: `server/src/TempoDesk.Application/Authentication/RateLimit/AuthenticationRateLimitRequest.cs`
- Create: `server/src/TempoDesk.Application/Authentication/RateLimit/AuthenticationRateLimitResult.cs`
- Create: `server/src/TempoDesk.Application/Authentication/RateLimit/IAuthenticationRateLimiter.cs`
- Create: `server/src/TempoDesk.Application/Authentication/RateLimit/AuthenticationRateLimitKeys.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/RateLimit/AuthenticationRateLimiter.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Create: `server/tests/TempoDesk.Application.Tests/Authentication/RateLimit/AuthenticationRateLimiterTests.cs`

**Interfaces:**
- Consumes: `IOptions<SecurityOptions>` and `TimeProvider`.
- Produces: `IAuthenticationRateLimiter.Acquire(AuthenticationRateLimitRequest)`.
- Produces: SHA-256 account partition keys without retaining raw email.

- [ ] **Step 1: Write failing threshold, recovery, and concurrency tests**

Use a manual `TimeProvider` and test options. Assert permits succeed exactly to
the threshold, the next call returns a positive retry, distinct accounts have
independent account quotas, one IP shares IP quota, advancing past the window
restores access, and 100 concurrent acquisitions allow exactly the configured
permit count.

- [ ] **Step 2: Run limiter tests to verify RED**

```bash
dotnet test server/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj --filter FullyQualifiedName~AuthenticationRateLimiterTests
```

Expected: compilation fails because limiter contracts do not exist.

- [ ] **Step 3: Add limiter contracts and account hashing**

Create enum policies `Register`, `Login`, `ForgotPassword`, and `Refresh`.
Create:

```csharp
public sealed record AuthenticationRateLimitRequest(
    AuthenticationRateLimitPolicy Policy,
    string IpAddress,
    string? AccountKey = null);

public sealed record AuthenticationRateLimitResult(
    bool Allowed,
    TimeSpan RetryAfter);

public interface IAuthenticationRateLimiter
{
    AuthenticationRateLimitResult Acquire(
        AuthenticationRateLimitRequest request);
}
```

`AuthenticationRateLimitKeys.ForAccount(email)` trims, converts with
`ToUpperInvariant`, hashes UTF-8 bytes using SHA-256, and returns lowercase hex.

- [ ] **Step 4: Implement thread-safe fixed windows**

Use a singleton implementation with a private lock and dictionary keyed by
`policy:dimension:value`. Each entry stores window start and count. Under the
lock, remove expired entries; inspect the IP and required account partitions;
return the longest remaining duration if either is full; otherwise increment
all required partitions atomically. Never log keys. Register it singleton.

- [ ] **Step 5: Run limiter tests to verify GREEN**

Run the Step 2 command.

Expected: threshold, composite-key, recovery, cleanup, and concurrency tests pass.

- [ ] **Step 6: Commit limiter engine**

```bash
git add server/src/TempoDesk.Application/Authentication/RateLimit server/src/TempoDesk.Infrastructure/Identity/RateLimit server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs server/tests/TempoDesk.Application.Tests/Authentication/RateLimit
git commit -m "feat(security): limit authentication attempts"
```

---

### Task 3: Enforce policies and return safe 429 responses

**Files:**
- Create: `server/src/TempoDesk.Api/Features/Authentication/RateLimit/AuthenticationRateLimitControllerExtensions.cs`
- Modify: `server/src/TempoDesk.Api/Features/Authentication/Registration/RegistrationController.cs`
- Modify: `server/src/TempoDesk.Api/Features/Authentication/Login/LoginController.cs`
- Modify: `server/src/TempoDesk.Api/Features/Authentication/PasswordReset/PasswordResetController.cs`
- Modify: `server/src/TempoDesk.Api/Features/Authentication/RefreshToken/RefreshTokenController.cs`
- Create: `server/tests/TempoDesk.Api.Tests/Authentication/RateLimit/AuthenticationRateLimitApiTests.cs`

**Interfaces:**
- Consumes: Task 2 limiter and SCRUM-26 password-reset API.
- Produces: 429 ProblemDetails with integer `Retry-After` and no service call.

- [ ] **Step 1: Write failing endpoint policy tests**

Configure tiny limits and a manual clock. For all four endpoints send
threshold+1 requests and assert the last is 429, service calls stop at the
threshold, and `Retry-After` is an integer at least 1. For login/forgot also
prove both account and IP partitions and window recovery.

- [ ] **Step 2: Run API rate-limit tests to verify RED**

```bash
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~AuthenticationRateLimitApiTests
```

Expected: requests continue reaching fakes because controllers do not acquire policies.

- [ ] **Step 3: Add the shared controller helper**

Build IP from `RemoteIpAddress?.ToString() ?? "unknown"`; hash optional email
with `AuthenticationRateLimitKeys.ForAccount`; acquire the policy. On denial set
`Retry-After` to `ceil(seconds)` with minimum 1 and return ProblemDetails 429,
title `Too Many Requests`, detail `Too many authentication attempts. Try again later.`

- [ ] **Step 4: Wire all approved policies**

Inject the limiter and acquire Register before `RegisterAsync`, Login before
`LoginAsync`, ForgotPassword before `ForgotPasswordAsync`, and Refresh before
`RefreshAsync`. Do not rate-limit reset-password because it is not in the
approved ticket scope.

- [ ] **Step 5: Run rate-limit and complete API tests**

```bash
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~AuthenticationRateLimitApiTests
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj
```

Expected: all policy tests and all API regressions pass.

- [ ] **Step 6: Commit endpoint enforcement**

```bash
git add server/src/TempoDesk.Api/Features/Authentication server/tests/TempoDesk.Api.Tests/Authentication
git commit -m "feat(api): enforce authentication rate limits"
```

---

### Task 4: Enrich security audit rows from request context

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/Audit/SecurityAuditContext.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/SecurityAuditLog.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Modify: `server/src/TempoDesk.Api/RequestBoundaryMiddleware.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/IdentityModelTests.cs`
- Create: `server/tests/TempoDesk.IntegrationTests/Authentication/Audit/AuthenticationAuditIntegrationTests.cs`

**Interfaces:**
- Produces: scoped `SecurityAuditContext` with IP, bounded User-Agent, and trace ID.
- Produces: automatic enrichment of every new `SecurityAuditLog` before save.

- [ ] **Step 1: Write failing mapping and enrichment tests**

Extend model assertions for `ip_address`, `user_agent`, `trace_id`, and
`metadata`. Through a database-backed API factory, send a known `traceparent`,
IP, and 1,100-character User-Agent with a failed login; assert the audit row
contains the IP, first 1,024 characters, and matching trace ID.

- [ ] **Step 2: Run audit tests to verify RED**

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~IdentityModelTests
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~AuthenticationAuditIntegrationTests
```

Expected: compilation/assertions fail because audit context fields are absent.

- [ ] **Step 3: Add audit context and entity mapping**

Create a scoped `SecurityAuditContext` with nullable `IPAddress IpAddress`,
`string UserAgent`, and `string TraceId`. Add the same fields to
`SecurityAuditLog` plus `string Metadata = "{}"`. Map IP as `inet`, metadata as
`jsonb`, trace ID max 100, and User-Agent as text. Register context scoped.

- [ ] **Step 4: Populate request context safely**

Inject the context into `RequestBoundaryMiddleware.InvokeAsync`. After the
Activity starts, assign remote IP, the first 1,024 User-Agent characters, and
the Activity trace ID. Never read Authorization, Cookie, or request body.

- [ ] **Step 5: Enrich rows before every save**

Inject `SecurityAuditContext` into `TempoDeskIdentityDbContext`. Override sync
and async SaveChanges; before delegating, enumerate Added `SecurityAuditLog`
entries and fill only null context fields. This keeps explicit event fields and
whitelisted metadata intact and supports non-HTTP empty contexts.

- [ ] **Step 6: Run audit tests to verify GREEN**

Run the Step 2 commands.

Expected: mapping and request-context enrichment tests pass.

- [ ] **Step 7: Commit audit enrichment**

```bash
git add server/src/TempoDesk.Application/Authentication/Audit server/src/TempoDesk.Infrastructure/Identity server/src/TempoDesk.Api/RequestBoundaryMiddleware.cs server/tests/TempoDesk.IntegrationTests/IdentityModelTests.cs server/tests/TempoDesk.IntegrationTests/Authentication/Audit
git commit -m "feat(security): enrich authentication audits"
```

---

### Task 5: Audit rate-limit denials and prove redaction

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/Audit/IAuthenticationAuditService.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/Audit/AuthenticationAuditService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Modify: `server/src/TempoDesk.Api/Features/Authentication/RateLimit/AuthenticationRateLimitControllerExtensions.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/Authentication/Audit/AuthenticationAuditIntegrationTests.cs`
- Modify: `server/tests/TempoDesk.Api.Tests/FoundationApiTests.cs`

**Interfaces:**
- Produces: `RecordRateLimitedAsync(AuthenticationRateLimitPolicy, CancellationToken)`.
- Produces: `auth.rate_limited` with metadata containing only policy name.

- [ ] **Step 1: Write failing denial-audit and redaction tests**

Trigger denial using distinctive email, password, cookie, bearer value, reset
and refresh token values. Assert the audit event is `auth.rate_limited`, failed,
has trace/IP/User-Agent, and metadata exactly contains the lowercase policy.
Concatenate request logs and audit fields/metadata; assert none contain email,
password, cookie/token/header values, JWT, or account hash.

- [ ] **Step 2: Run denial-audit tests to verify RED**

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~AuthenticationAuditIntegrationTests
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~LoggingCorrelates
```

Expected: the denial audit is absent.

- [ ] **Step 3: Implement the audit service**

Add `IAuthenticationAuditService.RecordRateLimitedAsync`. Its Infrastructure
implementation adds an unsuccessful `auth.rate_limited` row at the current
TimeProvider time with JSON metadata created only from:

```csharp
new { policy = policy.ToString().ToLowerInvariant() }
```

Save through the Identity DbContext so Task 4 enrichment applies. Register it
scoped. Update the rate-limit helper to await it before returning 429; never
pass IP, email, account key, credential, or token to this service.

- [ ] **Step 4: Run denial-audit tests to verify GREEN**

Run the Step 2 command.

Expected: denial audit and all sensitive-value scans pass.

- [ ] **Step 5: Run the complete server suite**

```bash
dotnet test server/TempoDesk.sln
```

Expected: zero failures across existing, SCRUM-26, limiter, and audit tests.

- [ ] **Step 6: Commit denial auditing**

```bash
git add server/src/TempoDesk.Application/Authentication/Audit server/src/TempoDesk.Infrastructure/Identity/Audit server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs server/src/TempoDesk.Api/Features/Authentication/RateLimit server/tests/TempoDesk.IntegrationTests/Authentication/Audit server/tests/TempoDesk.Api.Tests/FoundationApiTests.cs
git commit -m "feat(security): audit authentication throttling"
```

---

### Task 6: Verify SCRUM-27

**Files:**
- Verify only; modify a Task 1-5 file only when fresh verification exposes a defect.

**Interfaces:**
- Consumes: all SCRUM-26 and SCRUM-27 tasks.
- Produces: final test/build/format/config/redaction evidence.

- [ ] **Step 1: Run complete server tests**

```bash
dotnet test server/TempoDesk.sln
```

Expected: zero failures.

- [ ] **Step 2: Verify changed-file formatting**

Run `dotnet format server/TempoDesk.sln --verify-no-changes --include` with all
SCRUM-26/27 `.cs` files from `git diff --name-only development...HEAD`.

Expected: exit code 0 without modifying unrelated baseline files.

- [ ] **Step 3: Build Release**

```bash
dotnet build server/TempoDesk.sln --configuration Release --no-restore
```

Expected: zero warnings and zero errors.

- [ ] **Step 4: Inspect final diff and sensitive boundaries**

```bash
git diff development...HEAD --check
rg -n "Password|TokenHash|Authorization|Cookie|AccountKey" server/src/TempoDesk.Api/Features/Authentication/RateLimit server/src/TempoDesk.Application/Authentication/Audit server/src/TempoDesk.Infrastructure/Identity/Audit
git status --short
```

Expected: no sensitive value enters response/audit contracts, diff check emits
nothing, and the worktree contains only intentional files.
