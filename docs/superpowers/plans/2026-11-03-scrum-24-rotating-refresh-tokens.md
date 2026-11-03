# SCRUM-24 Rotating Refresh Tokens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Issue HttpOnly refresh-token cookies, rotate each token once, and atomically revoke the token family when a rotated token is reused.

**Architecture:** Extract JWT creation into a shared access-token issuer. Add an application token-service contract and refresh-token repository boundary; Infrastructure implements cryptographic token creation, EF mapping, PostgreSQL row locking, rotation, reuse detection, auditing, and family revocation. API controllers only translate token results to the existing access-token JSON contract and refresh-token cookies.

**Tech Stack:** .NET 10, ASP.NET Core MVC/Identity, EF Core 10, PostgreSQL 18, JWT bearer authentication, xUnit, Testcontainers.

## Global Constraints

- Raw refresh tokens are 32 random bytes encoded as unpadded base64url.
- Store only lowercase SHA-256 hashes; never log or persist raw refresh tokens, cookies, or JWTs.
- The cookie name is `tempodesk.refresh_token`, with `HttpOnly`, `SameSite=Strict`, path `/api/auth`, matching expiry, and `Secure` on HTTPS.
- `POST /api/auth/login` keeps its existing JSON response and additionally sets the initial refresh cookie.
- `POST /api/auth/refresh` has no body, rotates the cookie, and returns the existing access-token JSON response.
- Invalid, missing, malformed, expired, revoked, and reused tokens return the same generic HTTP 401 response and expire the cookie.
- Reuse revokes the whole family in the same database transaction.
- Concurrent use allows at most one rotation; a waiting duplicate detects reuse and revokes the family.
- Do not add Google login, logout, session management, device metadata, cleanup jobs, rate limiting, or a database migration.

---

### Task 1: Configuration and refresh-token persistence model

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/Configuration/FoundationOptions.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshToken.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs`
- Modify: `server/tests/TempoDesk.Application.Tests/ConfigurationTests.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/IdentityModelTests.cs`
- Modify: `docker-compose.yml`

**Interfaces:**
- Produces: `AuthenticationOptions.RefreshTokenDays`, default 30 and valid from 1 through 90.
- Produces: `DbSet<RefreshToken>` mapped to the existing `public.refresh_tokens` table.

- [ ] **Step 1: Write failing configuration and model tests**

Extend the valid authentication setup with `Authentication:RefreshTokenDays = 30`. Add invalid cases `0` and `91`, and assert the valid options expose 30.

In `IdentityModelTests`, assert the new entity maps `Id`, `UserId`, `FamilyId`, `TokenHash`, `ExpiresAt`, `UsedAt`, `RevokedAt`, `ReplacedByTokenId`, and `CreatedAt` to the existing snake-case columns and table.

- [ ] **Step 2: Run tests to verify RED**

Run: `dotnet test server/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj --filter FullyQualifiedName~ConfigurationTests`

Run: `dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~IdentityModelTests`

Expected: compilation FAIL because `RefreshTokenDays` and `RefreshToken` do not exist.

- [ ] **Step 3: Add the option, entity, and EF mapping**

Add:

```csharp
public int RefreshTokenDays { get; set; } = 30;
```

Include `o.RefreshTokenDays is >= 1 and <= 90` in enabled authentication validation and wire `Authentication__RefreshTokenDays: "30"` in Docker.

Create:

```csharp
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

Map it to `refresh_tokens`, configure the four existing indexes (`token_hash` unique, `user_id`, `family_id`, and `expires_at`), and add `DbSet<RefreshToken> RefreshTokens`.

- [ ] **Step 4: Run focused tests to verify GREEN**

Run the two Step 2 commands.

Expected: both PASS.

- [ ] **Step 5: Commit**

```bash
git add docker-compose.yml server/src/TempoDesk.Infrastructure/Configuration/FoundationOptions.cs server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshToken.cs server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs server/tests/TempoDesk.Application.Tests/ConfigurationTests.cs server/tests/TempoDesk.IntegrationTests/IdentityModelTests.cs
git commit -m "feat(auth): map refresh token persistence"
```

### Task 2: Shared token contracts, JWT issuer, and initial login issuance

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/RefreshToken/AuthenticationTokenResult.cs`
- Create: `server/src/TempoDesk.Application/Authentication/RefreshToken/IAuthenticationTokenService.cs`
- Create: `server/src/TempoDesk.Application/Authentication/RefreshToken/IRefreshTokenRepository.cs`
- Create: `server/src/TempoDesk.Application/Authentication/RefreshToken/NewRefreshToken.cs`
- Create: `server/src/TempoDesk.Application/Authentication/RefreshToken/RefreshTokenRotation.cs`
- Modify: `server/src/TempoDesk.Application/Authentication/Login/LoginResult.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/AccessTokenIssuer.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/AuthenticationTokenService.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshTokenRepository.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/Login/AccountLoginService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/Authentication/Login/LoginIntegrationTests.cs`

**Interfaces:**
- Produces: `IAuthenticationTokenService.IssueAsync(Guid userId, CancellationToken)` and `RefreshAsync(string rawToken, CancellationToken)`.
- Produces: `AuthenticationTokenResult` containing access token, access expiry seconds, raw refresh token, and refresh expiry.
- Produces: repository methods for initial persistence, malformed-attempt auditing, and atomic rotation.

- [ ] **Step 1: Write a failing login issuance integration test**

Extend the successful-login test to require a nonblank 43-character `result.RefreshToken` and a refresh expiry 30 days after the manual clock. Query `public.refresh_tokens` and assert exactly one row for the user, a 64-character lowercase hash, the expected family/expiry, and that neither the raw refresh token nor JWT appears in stored audit metadata.

- [ ] **Step 2: Run the test to verify RED**

Run: `dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~LoginIntegrationTests.SuccessfulLogin`

Expected: compilation FAIL because `LoginResult` has no refresh-token properties and initial refresh issuance is absent.

- [ ] **Step 3: Add the application contracts**

Create:

```csharp
public sealed record AuthenticationTokenResult(
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public interface IAuthenticationTokenService
{
    Task<AuthenticationTokenResult> IssueAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AuthenticationTokenResult?> RefreshAsync(
        string rawToken,
        CancellationToken cancellationToken = default);
}
```

Define repository contracts using scalar application records rather than Infrastructure entities:

```csharp
public sealed record NewRefreshToken(
    Guid Id,
    string TokenHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record RefreshTokenRotation(
    RefreshTokenRotation.Status Outcome,
    Guid? UserId)
{
    public enum Status { Succeeded, Missing, Invalid, Reused }
}

public interface IRefreshTokenRepository
{
    Task CreateAsync(
        Guid userId,
        Guid familyId,
        NewRefreshToken token,
        CancellationToken cancellationToken = default);

    Task<RefreshTokenRotation> RotateAsync(
        string tokenHash,
        NewRefreshToken replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task RecordRejectedAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
```

Extend `LoginResult` without breaking its existing access-token callers:

```csharp
public sealed record LoginResult(
    string? AccessToken,
    int ExpiresIn,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiresAt = null)
{
    public bool Succeeded => AccessToken is not null;
    public static LoginResult Rejected { get; } = new(null, 0);
}
```

- [ ] **Step 4: Extract access-token issuance and implement initial refresh issuance**

Move the existing JWT creation byte-for-byte into `AccessTokenIssuer.Issue(Guid userId)`, returning the JWT and configured `ExpiresIn`.

`AuthenticationTokenService.IssueAsync` generates the raw refresh token, hashes it, calls repository `CreateAsync` with a new token ID and family ID, then combines it with `AccessTokenIssuer` output.

`AccountLoginService` retains Identity credential checks and auditing, begins a transaction after successful credentials, calls `IssueAsync`, writes the existing successful login audit, saves, commits, and maps all four token-result properties into `LoginResult`.

Register the issuer, repository, and `IAuthenticationTokenService` as scoped services.

- [ ] **Step 5: Run login integration tests**

Run: `dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~LoginIntegrationTests`

Expected: PASS with the existing access-token assertions plus the new refresh-token assertions.

- [ ] **Step 6: Commit**

```bash
git add server/src/TempoDesk.Application/Authentication server/src/TempoDesk.Infrastructure/Identity server/tests/TempoDesk.IntegrationTests/Authentication/Login/LoginIntegrationTests.cs
git commit -m "feat(auth): issue refresh token on login"
```

### Task 3: Atomic rotation, reuse detection, and concurrency

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/AuthenticationTokenService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshTokenRepository.cs`
- Create: `server/tests/TempoDesk.IntegrationTests/Authentication/RefreshToken/RefreshTokenIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 2 token service and repository contracts.
- Produces: one-time rotation and family revocation with the same generic nullable service result.

- [ ] **Step 1: Write the failing valid-rotation test**

Create a PostgreSQL Testcontainer with the existing migrations. Issue an initial token through `IAuthenticationTokenService`, refresh it, and assert:

```csharp
Assert.NotNull(rotated);
Assert.NotEqual(initial.RefreshToken, rotated.RefreshToken);
Assert.NotEqual(initial.AccessToken, rotated.AccessToken);
```

Query the old row and replacement row: old `used_at` is set, old `replaced_by_token_id` equals replacement ID, family IDs match, replacement is active, and only hashes are stored.

- [ ] **Step 2: Run to verify RED**

Run: `dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~RefreshTokenIntegrationTests.ValidToken`

Expected: FAIL because `RefreshAsync` has no rotation implementation.

- [ ] **Step 3: Implement valid rotation**

Validate raw-token length at `1..512`, hash it, create replacement ID/hash/expiry, and call repository rotation. Blank or oversized values call `RecordRejectedAsync` so malformed attempts still produce a safe failed audit.

The repository begins a transaction, locks the candidate with parameterized `SELECT ... FOR UPDATE`, sets `UsedAt` and `ReplacedByTokenId`, inserts the replacement in the same family, writes successful `token.refresh` audit, saves, and commits. The service issues a new access token only for `Succeeded` with a known user ID.

- [ ] **Step 4: Verify valid rotation GREEN**

Run the Step 2 command.

Expected: PASS.

- [ ] **Step 5: Write failing invalid/reuse tests**

Add tests for missing, malformed, expired, and revoked tokens returning null. Rotate once, reuse the original, then assert every family row has `revoked_at` set and the replacement can no longer refresh. Assert audit events use `token.refresh` and `token.refresh.reuse` and contain no submitted raw token, hash, or JWT.

- [ ] **Step 6: Implement safe rejection and family revocation**

Inside the same row-lock transaction:

- missing/expired/revoked → failed `token.refresh` audit and reject;
- used/replaced → `ExecuteUpdateAsync` all non-revoked family rows to set `RevokedAt`, add failed `token.refresh.reuse` audit, commit, and reject.

- [ ] **Step 7: Write and run the concurrent refresh test**

Use two DI scopes and `Task.WhenAll` to refresh the same raw token. Assert no more than one result is non-null, all rows in the family are revoked after both finish, and neither returned/raw token can refresh again.

Run: `dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~RefreshTokenIntegrationTests`

Expected: all refresh-token integration tests PASS.

- [ ] **Step 8: Commit**

```bash
git add server/src/TempoDesk.Infrastructure/Identity/RefreshToken server/tests/TempoDesk.IntegrationTests/Authentication/RefreshToken
git commit -m "feat(auth): rotate refresh tokens and detect reuse"
```

### Task 4: HttpOnly cookie and refresh API

**Files:**
- Move: `server/src/TempoDesk.Api/Features/Authentication/Login/LoginResponseDto.cs` → `server/src/TempoDesk.Api/Features/Authentication/AccessTokenResponseDto.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/RefreshToken/RefreshTokenCookie.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/RefreshToken/RefreshTokenController.cs`
- Modify: `server/src/TempoDesk.Api/Features/Authentication/Login/LoginController.cs`
- Modify: `server/tests/TempoDesk.Api.Tests/Authentication/Login/LoginApiTests.cs`
- Create: `server/tests/TempoDesk.Api.Tests/Authentication/RefreshToken/RefreshTokenApiTests.cs`

**Interfaces:**
- Produces: shared `AccessTokenResponseDto` JSON contract.
- Produces: cookie helper append/delete behavior and `POST /api/auth/refresh`.

- [ ] **Step 1: Write failing login-cookie API test**

Update the fake login service to return `LoginResult` with access token, expiry seconds, raw refresh token, and refresh expiry. Assert the existing JSON is unchanged and `Set-Cookie` contains `tempodesk.refresh_token=<raw>`, `httponly`, `samesite=strict`, `path=/api/auth`, and an expiry, while the response body does not contain the refresh token.

- [ ] **Step 2: Run to verify RED**

Run: `dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~LoginApiTests`

Expected: FAIL because login does not set a cookie.

- [ ] **Step 3: Add the shared DTO and cookie helper**

Rename `LoginResponseDto` to `AccessTokenResponseDto` without changing JSON property names. Add a static cookie helper that appends the raw refresh token with the approved options and deletes it using the same path/SameSite/Secure settings.

Update `LoginController` to set the cookie only when the successful result contains both refresh-token values; keep access-token JSON sourced from the existing `AccessToken` and `ExpiresIn` properties.

- [ ] **Step 4: Verify login API GREEN**

Run the Step 2 command.

Expected: PASS.

- [ ] **Step 5: Write failing refresh endpoint tests**

With a fake `IAuthenticationTokenService`, assert:

- valid cookie → HTTP 200, unchanged access-token JSON, replacement `Set-Cookie`;
- missing or rejected cookie → identical generic HTTP 401 ProblemDetails and an expired cookie;
- request body is unnecessary and no response contains either raw refresh token.

- [ ] **Step 6: Implement `RefreshTokenController`**

Read only `Request.Cookies[RefreshTokenCookie.Name]`, call `RefreshAsync`, delete the cookie and return `Unauthorized()` for null, otherwise append replacement cookie and return `AccessTokenResponseDto`.

- [ ] **Step 7: Run authentication API tests**

Run: `dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter "FullyQualifiedName~LoginApiTests|FullyQualifiedName~RefreshTokenApiTests"`

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add server/src/TempoDesk.Api/Features/Authentication server/tests/TempoDesk.Api.Tests/Authentication
git commit -m "feat(auth): expose refresh token cookie endpoint"
```

### Task 5: Full verification

**Files:**
- Verify all SCRUM-24 files; modify only the task that owns a discovered defect.

- [ ] **Step 1: Verify formatting on changed C# files**

Run `dotnet format server/TempoDesk.sln --verify-no-changes --no-restore --include` with the changed Authentication, configuration, DbContext, and test files.

Expected: exit 0 without formatting unrelated legacy files.

- [ ] **Step 2: Run the full server test suite**

Run: `dotnet test server/TempoDesk.sln --no-restore`

Expected: all tests PASS, including concurrent Testcontainers coverage.

- [ ] **Step 3: Run the Release build**

Run: `dotnet build server/TempoDesk.sln -c Release --no-restore`

Expected: 0 warnings and 0 errors.

- [ ] **Step 4: Inspect secret handling and scope**

Run: `rg -n "RefreshToken|refresh_token|token.refresh" server/src server/tests docker-compose.yml`

Expected: raw token values appear only in transient result/cookie handling and test fixtures, never in logs, audit metadata, or persisted entity fields.

Run: `git diff --check && git status --short`

Expected: no whitespace errors and only intended SCRUM-24 changes.
