# SCRUM-20 Registration and Email Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add enumeration-safe registration, single-use email verification, resend, audit records, and API endpoints backed by the existing PostgreSQL schema.

**Architecture:** Application defines framework-free contracts. Infrastructure implements them with `UserManager<ApplicationUser>`, the existing Identity DbContext, cryptographic random tokens, and conditional PostgreSQL updates. API maps typed outcomes to stable HTTP responses; tests replace the deferred sender with an in-memory capture.

**Tech Stack:** .NET 10, ASP.NET Core Identity, EF Core 10, Npgsql/PostgreSQL 18, xUnit, Testcontainers.

## Global Constraints

- Database-first: do not run EF migrations or edit files under `Persistence/Generated`.
- Do not add roles or passkey tables; keep `IdentitySchemaVersions.Version2`.
- Raw passwords and verification tokens must never be persisted or logged.
- Register and resend responses must not reveal whether an email exists.
- `/docs` is local-only and must never be staged or committed.
- Email templates, link construction, and local capture UI remain outside SCRUM-20.

---

### Task 1: Create the feature branch and registration contracts

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/RegistrationContracts.cs`
- Test: `server/tests/TempoDesk.IntegrationTests/RegistrationIntegrationTests.cs`

**Interfaces:**
- Produces: `IAccountRegistrationService`, `IEmailVerificationSender`, `RegistrationResult`, and `VerificationEmail`.
- `IAccountRegistrationService` exposes `RegisterAsync`, `VerifyEmailAsync`, and `ResendVerificationAsync`.

- [ ] **Step 1: Create the branch from current `development`**

```bash
git switch -c sprint-1/feat/SCRUM-20
```

- [ ] **Step 2: Write a compiling-contract test before the contracts exist**

Create `RegistrationIntegrationTests.cs`. It must fail to compile because the contracts do not exist.

```csharp
using TempoDesk.Application.Authentication;

namespace TempoDesk.IntegrationTests;

public sealed class RegistrationIntegrationTests
{
    [Fact]
    public void VerificationSenderContractCarriesTheRawTokenOnlyForDelivery()
    {
        IEmailVerificationSender sender = new CapturingVerificationSender();
        var message = new VerificationEmail(
            "person@example.com",
            "raw-token",
            DateTimeOffset.UtcNow.AddMinutes(30));

        Assert.True(sender.SendAsync(message, CancellationToken.None).Result);
        Assert.Equal(message, ((CapturingVerificationSender)sender).LastMessage);
    }

private sealed class CapturingVerificationSender : IEmailVerificationSender
{
    public VerificationEmail? LastMessage { get; private set; }

    public Task<bool> SendAsync(
        VerificationEmail message,
        CancellationToken cancellationToken)
    {
        LastMessage = message;
        return Task.FromResult(true);
    }
}
}
```

- [ ] **Step 3: Verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.IntegrationTests --filter FullyQualifiedName~RegistrationIntegrationTests -m:1
```

Expected: compile failure naming `IEmailVerificationSender` or `VerificationEmail`.

- [ ] **Step 4: Add the framework-free contracts**

Create `RegistrationContracts.cs`:

```csharp
namespace TempoDesk.Application.Authentication;

public interface IAccountRegistrationService
{
    Task<RegistrationResult> RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyEmailAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task ResendVerificationAsync(
        string email,
        CancellationToken cancellationToken = default);
}

public sealed record RegistrationResult(
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static RegistrationResult Accepted { get; } =
        new(new Dictionary<string, string[]>());
}

public sealed record VerificationEmail(
    string Email,
    string Token,
    DateTimeOffset ExpiresAt);

public interface IEmailVerificationSender
{
    Task<bool> SendAsync(
        VerificationEmail message,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Verify the contract test is GREEN**

Run the same filtered test. Expected: one passing contract test.

---

### Task 2: Map token and audit tables and implement registration

**Files:**
- Create: `server/src/TempoDesk.Infrastructure/Identity/EmailVerificationToken.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/SecurityAuditLog.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/AccountRegistrationService.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/DeferredEmailVerificationSender.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Test: `server/tests/TempoDesk.IntegrationTests/RegistrationIntegrationTests.cs`

**Interfaces:**
- Consumes: the Application contracts from Task 1.
- Produces: scoped `IAccountRegistrationService` and overrideable `IEmailVerificationSender` DI registrations.

- [ ] **Step 1: Write the failing PostgreSQL registration test**

Use `PostgreSqlBuilder("postgres:18.6-bookworm")`, apply the two existing Identity SQL migrations from test output, register a runtime role with table and sequence privileges, replace `IEmailVerificationSender` with `CapturingVerificationSender`, and resolve `IAccountRegistrationService`.

The test must assert:

```csharp
var result = await registration.RegisterAsync(
    "Person@Example.com",
    "Correct-Horse-7-Battery!");

Assert.True(result.Succeeded);
Assert.NotNull(sender.LastMessage);
Assert.Equal("Person@Example.com", sender.LastMessage.Email);

var user = await users.FindByEmailAsync("person@example.com");
Assert.NotNull(user);
Assert.False(user.EmailConfirmed);

var stored = await databaseContext.EmailVerificationTokens.SingleAsync();
Assert.NotEqual(sender.LastMessage.Token, stored.TokenHash);
Assert.Equal(64, stored.TokenHash.Length);
Assert.Equal(user.Id, stored.UserId);
```

- [ ] **Step 2: Verify RED**

Run the filtered integration test. Expected: DI cannot resolve `IAccountRegistrationService`.

- [ ] **Step 3: Add persistence entities**

`EmailVerificationToken` contains exactly `Id`, `UserId`, `TokenHash`, `ExpiresAt`, `UsedAt`, and `CreatedAt`. `SecurityAuditLog` contains `Id`, nullable `UserId`, `EventType`, `Succeeded`, and `OccurredAt`; database defaults fill unmapped nullable/default columns.

- [ ] **Step 4: Add exact EF mappings**

Expose:

```csharp
public DbSet<EmailVerificationToken> EmailVerificationTokens =>
    Set<EmailVerificationToken>();

public DbSet<SecurityAuditLog> SecurityAuditLogs =>
    Set<SecurityAuditLog>();
```

Map them to `public.email_verification_tokens` and `public.security_audit_logs`, including the existing index names. Configure the audit ID with `ValueGeneratedOnAdd()`.

- [ ] **Step 5: Implement token generation and registration**

`AccountRegistrationService` receives `UserManager<ApplicationUser>`, `TempoDeskIdentityDbContext`, `IEmailVerificationSender`, and `TimeProvider`.

Token creation must be exactly:

```csharp
var bytes = RandomNumberGenerator.GetBytes(32);
var token = Convert.ToBase64String(bytes)
    .TrimEnd('=')
    .Replace('+', '-')
    .Replace('/', '_');
var hash = Convert.ToHexString(
    SHA256.HashData(Encoding.UTF8.GetBytes(token)))
    .ToLowerInvariant();
```

Create an unconfirmed `ApplicationUser`, call `UserManager.CreateAsync`, insert the token and `user.registration`/`email.verification.issued` audits in the same transaction, commit, then call the sender. If delivery returns false, insert `email.verification.delivery` with `Succeeded=false` but still return accepted.

Sequential existing confirmed users return accepted without issuing a token. Existing unconfirmed users call the resend path. Convert Identity duplicate-user/email errors to accepted; return all other Identity errors in `RegistrationResult.Errors`.

- [ ] **Step 6: Register services**

Use `TryAddScoped<IEmailVerificationSender, DeferredEmailVerificationSender>()`, add scoped `IAccountRegistrationService`, and `TryAddSingleton(TimeProvider.System)`. The deferred sender returns `false` without logging the message.

- [ ] **Step 7: Verify GREEN**

Run the filtered integration test. Expected: one test passes and the raw token exists only in the capturing fake.

- [ ] **Step 8: Commit the independently working registration slice**

```bash
git add server/src/TempoDesk.Application/Authentication \
  server/src/TempoDesk.Infrastructure/Identity \
  server/tests/TempoDesk.IntegrationTests/RegistrationIntegrationTests.cs
git commit -m "feat: add secure account registration"
```

---

### Task 3: Add single-use verification and resend

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/Identity/AccountRegistrationService.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/RegistrationIntegrationTests.cs`

**Interfaces:**
- Consumes: `VerifyEmailAsync` and `ResendVerificationAsync` already defined in Task 1.
- Produces: atomic single-use verification and replacement-token behavior.

- [ ] **Step 1: Add failing lifecycle assertions**

Extend the PostgreSQL test with independent random users and assert:

```csharp
Assert.True(await registration.VerifyEmailAsync(firstToken));
Assert.False(await registration.VerifyEmailAsync(firstToken));
Assert.True((await users.FindByEmailAsync(email))!.EmailConfirmed);
```

Advance a test `TimeProvider` by 31 minutes and assert an unconsumed token fails. For resend, capture the first token, request resend, capture the replacement, assert the values differ, assert the first fails, and assert the replacement succeeds.

Also register the same normalized email twice and assert only one `users` row exists.

- [ ] **Step 2: Verify RED**

Run the filtered integration test. Expected: verification returns false or throws because it is not implemented.

- [ ] **Step 3: Implement atomic verification**

Hash the submitted token, find its user ID without tracking, start a transaction, then claim it with:

```csharp
var claimed = await context.EmailVerificationTokens
    .Where(value => value.TokenHash == hash &&
        value.UsedAt == null && value.ExpiresAt > now)
    .ExecuteUpdateAsync(update => update
        .SetProperty(value => value.UsedAt, now), cancellationToken);
```

Continue only when `claimed == 1`. Load the user, reject missing/already-confirmed users, set `EmailConfirmed=true` and `UpdatedAt=now`, call `UserManager.UpdateAsync`, add a success audit, and commit. Every invalid path inserts the same failure event and returns false without exposing the reason.

- [ ] **Step 4: Implement resend**

Find by normalized email. Missing and confirmed users only add a generic resend audit. For an unconfirmed user, set `used_at=now` on every unused token for that user, create a replacement, commit, and deliver after commit. Preserve the same generic external result for every path.

- [ ] **Step 5: Verify GREEN and security properties**

Run the filtered integration test and query `security_audit_logs`. Assert neither the password nor any raw token appears in `metadata::text`, and assert the expected event types exist.

- [ ] **Step 6: Commit the verification lifecycle**

```bash
git add server/src/TempoDesk.Infrastructure/Identity/AccountRegistrationService.cs \
  server/tests/TempoDesk.IntegrationTests/RegistrationIntegrationTests.cs
git commit -m "feat: add single-use email verification"
```

---

### Task 4: Expose enumeration-safe authentication APIs

**Files:**
- Create: `server/src/TempoDesk.Api/Controllers/RegistrationController.cs`
- Create: `server/tests/TempoDesk.Api.Tests/RegistrationApiTests.cs`

**Interfaces:**
- Consumes: `IAccountRegistrationService`.
- Produces: `POST /api/auth/register`, `/api/auth/verify-email`, and `/api/auth/resend-verification`.

- [ ] **Step 1: Write API tests with a fake application service**

Replace `IAccountRegistrationService` in a `WebApplicationFactory<Program>`. Assert:

- valid register returns `202` and the generic message;
- duplicate and new scenarios have byte-for-byte equivalent response bodies;
- invalid password returns validation problem `400` without containing the password;
- valid verify returns `204`;
- invalid/expired/used verify all return the same validation problem `400`;
- resend always returns the same `202` body.

- [ ] **Step 2: Verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.Api.Tests --filter FullyQualifiedName~RegistrationApiTests -m:1
```

Expected: `404 Not Found` because the controller does not exist.

- [ ] **Step 3: Implement the controller**

Use `[ApiController]` and `[Route("api/auth")]`. Request models use `[Required]`, `[EmailAddress]`, and do not override `ToString()`.

Both register and resend return:

```csharp
Accepted(new
{
    message = "If the address can be verified, an email will be sent."
});
```

Map Identity validation errors to `ValidationProblem`; map invalid verification to a single `token` validation error. Never include the submitted password, email-existence state, or token in responses.

- [ ] **Step 4: Verify GREEN**

Run the filtered API tests. Expected: all registration API tests pass.

- [ ] **Step 5: Commit the API slice**

```bash
git add server/src/TempoDesk.Api/Controllers/RegistrationController.cs \
  server/tests/TempoDesk.Api.Tests/RegistrationApiTests.cs
git commit -m "feat: expose registration verification APIs"
```

---

### Task 5: Full verification and ticket readiness

**Files:**
- Verify all files changed by Tasks 1–4.

- [ ] **Step 1: Run formatting and whitespace checks**

```bash
git diff --check
dotnet format server/TempoDesk.sln --verify-no-changes --no-restore
```

- [ ] **Step 2: Run all .NET tests in Release**

```bash
dotnet test server/TempoDesk.sln --configuration Release -m:1 --disable-build-servers
```

Expected: zero failed tests and zero build warnings.

- [ ] **Step 3: Verify database-first safeguards**

```bash
bash database/tests/migrator.sh
```

Expected: empty-schema, repeated-run, scaffold, checksum, and rollback checks all print `PASS`.

- [ ] **Step 4: Inspect dependency boundaries and secrets**

```bash
rg -n "Microsoft.AspNetCore.Identity|Microsoft.EntityFrameworkCore|Npgsql" \
  server/src/TempoDesk.Application server/src/TempoDesk.Domain
rg -n "Password=|verification.*token|TokenHash" \
  server/src/TempoDesk.Api server/src/TempoDesk.Infrastructure
git status --short
```

Expected: no framework dependency in Domain/Application; no hard-coded credential or raw-token logging; no `/docs` entry in Git status because it is ignored.

- [ ] **Step 5: Review commits without merging or pushing automatically**

```bash
git log --oneline development..HEAD
git diff --stat development...HEAD
```

Report the verification evidence and wait for explicit user authorization before push, PR, merge, or Jira status changes.
