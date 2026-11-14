# SCRUM-26 Forgot and Reset Password Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add enumeration-safe forgot-password and single-use reset-password flows that email hashed reset tokens, enforce Identity password policy, and revoke every refresh-token session after success.

**Architecture:** Map the existing password-reset table and add a focused application service implemented with Identity, EF transactions, row locks, and the existing session service. Controllers translate neutral/validation results; SMTP delivery uses the existing reset template and occurs only after token persistence commits.

**Tech Stack:** .NET 10, ASP.NET Core MVC/Identity, EF Core 10, PostgreSQL 18, Npgsql, SMTP, xUnit, Testcontainers.

## Global Constraints

- Do not add a database migration; `public.password_reset_tokens` already exists.
- Raw tokens are 32 random bytes encoded as unpadded base64url and expire after 30 minutes.
- Persist only lowercase SHA-256 token hashes.
- Forgot-password always returns the same HTTP 202 contract for existing and missing accounts.
- Invalid, expired, reused, and malformed reset tokens share one safe HTTP 400 token error.
- Password-policy failure must not consume the token.
- Successful reset atomically changes the password, consumes the token, and revokes every refresh-token family.
- Never log, audit, or return passwords, raw tokens, hashes, cookies, Authorization headers, or JWTs.
- Do not add frontend screens, rate limits, access-token denylisting, cleanup jobs, dependencies, or schema changes.

---

### Task 1: Map reset-token persistence and add email delivery

**Files:**
- Create: `server/src/TempoDesk.Infrastructure/Identity/PasswordReset/PasswordResetToken.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs`
- Create: `server/src/TempoDesk.Application/Authentication/PasswordReset/PasswordResetEmail.cs`
- Create: `server/src/TempoDesk.Application/Authentication/PasswordReset/IPasswordResetEmailSender.cs`
- Create: `server/src/TempoDesk.Infrastructure/Email/Authentication/SmtpPasswordResetEmailSender.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/IdentityModelTests.cs`
- Modify: `server/tests/TempoDesk.Application.Tests/Authentication/Email/EmailDeliveryTests.cs`

**Interfaces:**
- Produces: EF entity `PasswordResetToken` mapped to the existing table and indexes.
- Produces: `PasswordResetEmail(string Email, string Token)`.
- Produces: `IPasswordResetEmailSender.SendAsync(PasswordResetEmail, CancellationToken)` returning `bool`.

- [ ] **Step 1: Write the failing model test**

Extend `IdentityModelTests` to require table `password_reset_tokens` and exact
columns `id`, `user_id`, `token_hash`, `expires_at`, `used_at`, and
`created_at`, plus the existing unique hash/user/expiry indexes.

```csharp
var resetToken = model.FindEntityType(typeof(PasswordResetToken));
Assert.Equal("password_reset_tokens", resetToken?.GetTableName());
Assert.Equal("token_hash", resetToken
    ?.FindProperty(nameof(PasswordResetToken.TokenHash))?.GetColumnName());
```

- [ ] **Step 2: Run the model test to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~IdentityModelTests
```

Expected: compilation fails because `PasswordResetToken` does not exist.

- [ ] **Step 3: Add the entity and mapping**

Create:

```csharp
namespace TempoDesk.Infrastructure.Identity;

public sealed class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

Add `DbSet<PasswordResetToken> PasswordResetTokens` and map all columns/indexes
using the names from `202610271944_create_indentity_schema.sql`.

- [ ] **Step 4: Run the model test to verify GREEN**

Run the Step 2 command.

Expected: the model test passes.

- [ ] **Step 5: Write failing reset-email delivery tests**

Add tests beside the existing verification sender tests. With email disabled,
assert `SendAsync` returns false. With a local SMTP test server or existing
Mailpit fixture, assert the subject/link use `PasswordReset` and the token is
URL encoded.

```csharp
var sent = await sender.SendAsync(
    new PasswordResetEmail("person@example.com", "reset/+ token"),
    CancellationToken.None);
Assert.True(sent);
```

- [ ] **Step 6: Run email tests to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj --filter FullyQualifiedName~EmailDeliveryTests
```

Expected: compilation fails because the reset-email contract and sender do not exist.

- [ ] **Step 7: Implement and register reset email delivery**

Create the record/interface:

```csharp
public sealed record PasswordResetEmail(string Email, string Token);

public interface IPasswordResetEmailSender
{
    Task<bool> SendAsync(
        PasswordResetEmail message,
        CancellationToken cancellationToken);
}
```

Implement `SmtpPasswordResetEmailSender` with the same safe SMTP setup as
`SmtpEmailVerificationSender`, but build content with:

```csharp
var content = AuthenticationEmailTemplates.PasswordReset(
    new Uri(options.FrontendBaseUrl), message.Token);
```

Register it scoped in `IdentityConfiguration`.

- [ ] **Step 8: Run email tests to verify GREEN**

Run the Step 6 command.

Expected: all email delivery tests pass.

- [ ] **Step 9: Commit persistence and email delivery**

```bash
git add server/src/TempoDesk.Application/Authentication/PasswordReset server/src/TempoDesk.Infrastructure/Identity/PasswordReset server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs server/src/TempoDesk.Infrastructure/Email/Authentication/SmtpPasswordResetEmailSender.cs server/tests/TempoDesk.IntegrationTests/IdentityModelTests.cs server/tests/TempoDesk.Application.Tests/Authentication/Email/EmailDeliveryTests.cs
git commit -m "feat(auth): add password reset persistence"
```

---

### Task 2: Issue neutral single-use reset tokens

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/PasswordReset/IAccountPasswordResetService.cs`
- Create: `server/src/TempoDesk.Application/Authentication/PasswordReset/ResetPasswordResult.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/PasswordReset/AccountPasswordResetService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Create: `server/tests/TempoDesk.IntegrationTests/Authentication/PasswordReset/PasswordResetIntegrationTests.cs`

**Interfaces:**
- Consumes: `UserManager<ApplicationUser>`, `TempoDeskIdentityDbContext`, `IPasswordResetEmailSender`, and `TimeProvider`.
- Produces: `IAccountPasswordResetService.ForgotPasswordAsync(string email, CancellationToken)`.
- Produces for Task 3: `ResetPasswordAsync(string token, string newPassword, CancellationToken)` and `ResetPasswordResult`.

- [ ] **Step 1: Write failing forgot-password integration tests**

Create a PostgreSQL Testcontainer fixture following the existing auth integration
tests. Assert:

- existing and missing email calls both complete without an account-existence result;
- existing account stores one 64-character lowercase hash and never the raw token;
- token expiry is exactly 30 minutes from the manual clock;
- a second request marks the first token used and creates one new unused token;
- email sender receives only the existing account's email and raw token;
- audits use `password.reset.requested`, with user ID only when known.

Use a recording fake:

```csharp
private sealed class RecordingPasswordResetEmailSender
    : IPasswordResetEmailSender
{
    public List<PasswordResetEmail> Messages { get; } = [];

    public Task<bool> SendAsync(
        PasswordResetEmail message,
        CancellationToken cancellationToken)
    {
        Messages.Add(message);
        return Task.FromResult(true);
    }
}
```

- [ ] **Step 2: Run forgot-password tests to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~PasswordResetIntegrationTests.Forgot
```

Expected: compilation fails because `IAccountPasswordResetService` does not exist.

- [ ] **Step 3: Add the application service contract and result**

Create:

```csharp
public interface IAccountPasswordResetService
{
    Task ForgotPasswordAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<ResetPasswordResult> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public sealed record ResetPasswordResult(
    bool Succeeded,
    bool TokenRejected,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static ResetPasswordResult Success { get; } =
        new(true, false, new Dictionary<string, string[]>());
    public static ResetPasswordResult InvalidToken { get; } =
        new(false, true, new Dictionary<string, string[]>());
}
```

- [ ] **Step 4: Implement forgot-password issuance**

Implement `AccountPasswordResetService` with a 30-minute lifetime. For missing
users, add an unsuccessful `password.reset.requested` audit with null user ID
and save. For an existing user:

1. begin a transaction;
2. lock the user with parameterized `SELECT ... FOR UPDATE`;
3. mark unused reset tokens used at `now`;
4. generate/hash a 32-byte base64url token;
5. add the new row and successful audit;
6. save and commit;
7. call the sender after commit and ignore its boolean for the external result.

Register `IAccountPasswordResetService` as scoped.

- [ ] **Step 5: Run forgot-password tests to verify GREEN**

Run the Step 2 command.

Expected: all forgot-password integration tests pass.

- [ ] **Step 6: Commit token issuance**

```bash
git add server/src/TempoDesk.Application/Authentication/PasswordReset server/src/TempoDesk.Infrastructure/Identity/PasswordReset server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs server/tests/TempoDesk.IntegrationTests/Authentication/PasswordReset
git commit -m "feat(auth): issue password reset tokens"
```

---

### Task 3: Reset password atomically and revoke sessions

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/Identity/PasswordReset/AccountPasswordResetService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshTokenRepository.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/Authentication/PasswordReset/PasswordResetIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 2 service/result and `IAuthenticationSessionService.RevokeAllAsync(Guid, CancellationToken)`.
- Produces: one-time, policy-validating `ResetPasswordAsync` with atomic session revocation.
- Changes: `RevokeAllSessionsAsync` joins `context.Database.CurrentTransaction` when present and owns/commits a transaction only when none exists.

- [ ] **Step 1: Write failing valid/invalid reset tests**

Add tests that issue a token through forgot-password, then assert:

```csharp
var result = await resets.ResetPasswordAsync(rawToken, NewPassword);
Assert.True(result.Succeeded);
Assert.True((await login.LoginAsync(email, NewPassword)).Succeeded);
Assert.False((await login.LoginAsync(email, OldPassword)).Succeeded);
Assert.Null(await tokens.RefreshAsync(oldRefreshToken));
```

Add separate tests for blank, oversized, unknown, expired, and already-used
tokens returning `TokenRejected`, plus weak password errors preserving token
usability.

- [ ] **Step 2: Run reset tests to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~PasswordResetIntegrationTests.Reset
```

Expected: tests fail because `ResetPasswordAsync` has no implementation.

- [ ] **Step 3: Implement token locking and password update**

Reject blank/over-512-character raw tokens uniformly. Hash valid input, begin a
transaction, and lock the token row by hash with `FOR UPDATE`. Reject if absent,
used, or expired.

Load the user, set `UsedAt = now`, then use Identity's
`RemovePasswordAsync(user)` and `AddPasswordAsync(user, newPassword)` inside the
same transaction. If Identity returns password-policy errors, roll back and map
errors by code into `ResetPasswordResult`; the token remains unused.

Call `IAuthenticationSessionService.RevokeAllAsync(user.Id)` and add successful
`password.reset` audit before saving and committing. Every rejected token writes
an unsuccessful `password.reset` audit without raw/hash data.

- [ ] **Step 4: Make session revocation transaction-aware**

In `RefreshTokenRepository.RevokeAllSessionsAsync`, use:

```csharp
var transaction = context.Database.CurrentTransaction is null
    ? await context.Database.BeginTransactionAsync(cancellationToken)
    : null;
```

Run the existing update/audit/save. Commit and dispose only the locally created
transaction. This preserves existing standalone behavior while joining the
password-reset transaction.

- [ ] **Step 5: Run reset tests to verify GREEN**

Run the Step 2 command.

Expected: valid, invalid, expired, reused, weak-password, login, and session
revocation tests pass.

- [ ] **Step 6: Write and verify concurrent reset test**

Run two service scopes concurrently with the same token and assert exactly one
result succeeds, the other rejects the token, the new password works, and all
sessions remain revoked.

Run:

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~PasswordResetIntegrationTests.Concurrent
```

Expected: one success, one rejection, zero active refresh-token rows.

- [ ] **Step 7: Run auth integration regression tests**

Run:

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter "FullyQualifiedName~PasswordResetIntegrationTests|FullyQualifiedName~LoginIntegrationTests|FullyQualifiedName~RefreshTokenIntegrationTests"
```

Expected: all selected tests pass.

- [ ] **Step 8: Commit atomic reset**

```bash
git add server/src/TempoDesk.Infrastructure/Identity/PasswordReset server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshTokenRepository.cs server/tests/TempoDesk.IntegrationTests/Authentication/PasswordReset
git commit -m "feat(auth): reset passwords and revoke sessions"
```

---

### Task 4: Expose forgot/reset password APIs

**Files:**
- Create: `server/src/TempoDesk.Api/Features/Authentication/PasswordReset/ForgotPasswordRequestDto.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/PasswordReset/ResetPasswordRequestDto.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/PasswordReset/PasswordResetController.cs`
- Create: `server/tests/TempoDesk.Api.Tests/Authentication/PasswordReset/PasswordResetApiTests.cs`

**Interfaces:**
- Consumes: `IAccountPasswordResetService`.
- Produces: `POST /api/auth/forgot-password` and `POST /api/auth/reset-password`.

- [ ] **Step 1: Write failing API tests**

With a fake reset service, assert existing/missing forgot scenarios have the
same 202 body, malformed email returns 400 without service invocation, success
reset returns 204, invalid token maps to the exact safe token error, and policy
errors map to validation ProblemDetails.

```csharp
Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
Assert.Equal(
    "{\"message\":\"If the account exists, a password reset email will be sent.\"}",
    await response.Content.ReadAsStringAsync());
```

- [ ] **Step 2: Run API tests to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~PasswordResetApiTests
```

Expected: requests return HTTP 404 because the controller does not exist.

- [ ] **Step 3: Add DTOs and controller**

Use `[Required, EmailAddress, MaxLength(256)]` for email, `[Required,
MaxLength(512)]` for token, and `[Required, MinLength(12), MaxLength(1024)]`
for new password.

The forgot action always calls `ForgotPasswordAsync` then returns the one static
accepted object. The reset action returns 204 on success; for token rejection it
adds only `ModelState["token"] = "The reset token is invalid or expired."`; for
password-policy errors it copies the result dictionary into ModelState.

- [ ] **Step 4: Run password-reset API tests to verify GREEN**

Run the Step 2 command.

Expected: all password-reset API tests pass.

- [ ] **Step 5: Run the complete API project**

Run:

```bash
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj
```

Expected: every API test passes.

- [ ] **Step 6: Commit APIs**

```bash
git add server/src/TempoDesk.Api/Features/Authentication/PasswordReset server/tests/TempoDesk.Api.Tests/Authentication/PasswordReset
git commit -m "feat(api): expose password reset flow"
```

---

### Task 5: Verify SCRUM-26

**Files:**
- Verify only; modify a Task 1-4 file only if verification exposes a defect.

**Interfaces:**
- Consumes: Tasks 1-4.
- Produces: fresh test/build/format and sensitive-data evidence.

- [ ] **Step 1: Run all server tests**

```bash
dotnet test server/TempoDesk.sln
```

Expected: zero failures.

- [ ] **Step 2: Verify formatting on changed files**

Run `dotnet format server/TempoDesk.sln --verify-no-changes --include` with all
SCRUM-26 `.cs` files reported by `git diff --name-only development...HEAD`.

Expected: exit code 0. Do not reformat unrelated baseline files.

- [ ] **Step 3: Build Release**

```bash
dotnet build server/TempoDesk.sln --configuration Release --no-restore
```

Expected: zero warnings and zero errors.

- [ ] **Step 4: Check secret boundaries and worktree**

```bash
git diff --check
rg -n "Password|TokenHash|RefreshToken|Authorization|Cookie" server/src/TempoDesk.Api/Features/Authentication/PasswordReset server/src/TempoDesk.Application/Authentication/PasswordReset
git status --short
```

Expected: no password/token/hash property appears in response DTOs or audit
metadata; only intentional source/test changes are present.
