# SCRUM-25 Session Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add idempotent logout, authenticated session listing, single-session revocation, and account-wide revocation using refresh-token families as sessions.

**Architecture:** Carry bounded request metadata through login into the existing refresh-token rows, and preserve it during rotation. Add a focused application session service backed by the existing refresh-token repository; controllers only extract cookies, request metadata, and the authenticated user ID before translating service results to HTTP responses.

**Tech Stack:** .NET 10, ASP.NET Core MVC/Identity/JWT bearer authentication, EF Core 10, PostgreSQL 18, Npgsql, xUnit, Testcontainers.

## Global Constraints

- Each refresh-token `family_id` is one session; do not add a session table or migration.
- Store at most 1,024 characters of the raw `User-Agent`; do not add a user-agent parser or dependency.
- Store the login IP address and leave `device_name` null.
- Never expose or audit raw refresh tokens, hashes, cookies, JWTs, user agents, or IP addresses.
- Logout is cookie-scoped, does not require a bearer token, always deletes the cookie, and always returns HTTP 204.
- Session list and explicit revocation derive the user ID only from the authenticated principal.
- Missing and foreign session IDs both return HTTP 404.
- Repeated owned revocation and revoke-all return HTTP 204 without changing the original `revoked_at`.
- Revoking refresh-token families does not denylist an already-issued access token.
- Use test-first red-green cycles and do not add frontend session-management UI.

---

### Task 1: Capture and preserve login session metadata

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/Session/SessionMetadata.cs`
- Modify: `server/src/TempoDesk.Application/Authentication/Login/IAccountLoginService.cs`
- Modify: `server/src/TempoDesk.Application/Authentication/RefreshToken/IAuthenticationTokenService.cs`
- Modify: `server/src/TempoDesk.Application/Authentication/RefreshToken/IRefreshTokenRepository.cs`
- Modify: `server/src/TempoDesk.Api/Features/Authentication/Login/LoginController.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/Login/AccountLoginService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/AuthenticationTokenService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshTokenRepository.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshToken.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/TempoDeskIdentityDbContext.cs`
- Modify: `server/tests/TempoDesk.Api.Tests/Authentication/Login/LoginApiTests.cs`
- Modify: `server/tests/TempoDesk.Api.Tests/Authentication/RefreshToken/RefreshTokenApiTests.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/IdentityModelTests.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/Authentication/Login/LoginIntegrationTests.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/Authentication/RefreshToken/RefreshTokenIntegrationTests.cs`

**Interfaces:**
- Produces: `SessionMetadata(string? UserAgent, IPAddress? IpAddress)`.
- Produces: `IAccountLoginService.LoginAsync(string email, string password, SessionMetadata metadata, CancellationToken cancellationToken = default)`.
- Produces: `IAuthenticationTokenService.IssueAsync(Guid userId, SessionMetadata metadata, CancellationToken cancellationToken = default)`.
- Produces: persisted and rotation-preserved `RefreshToken.UserAgent`, `RefreshToken.IpAddress`, and nullable `RefreshToken.DeviceName`.

- [ ] **Step 1: Write failing EF mapping and login API tests**

Extend `IdentityModelTests` with exact column assertions:

```csharp
Assert.Equal("device_name", refreshToken
    ?.FindProperty(nameof(RefreshToken.DeviceName))?.GetColumnName());
Assert.Equal("user_agent", refreshToken
    ?.FindProperty(nameof(RefreshToken.UserAgent))?.GetColumnName());
Assert.Equal("ip_address", refreshToken
    ?.FindProperty(nameof(RefreshToken.IpAddress))?.GetColumnName());
```

Extend the successful test in `LoginApiTests` to send a 1,100-character user
agent and assert the fake login service receives the first 1,024 characters and
the request remote IP:

```csharp
request.Headers.UserAgent.ParseAdd(new string('a', 1100));
using var response = await client.SendAsync(request);

Assert.Equal(new string('a', 1024), service.Metadata?.UserAgent);
Assert.NotNull(service.Metadata?.IpAddress);
```

Change the fake method signature and capture `SessionMetadata`:

```csharp
public SessionMetadata? Metadata { get; private set; }

public Task<LoginResult> LoginAsync(
    string email,
    string password,
    SessionMetadata metadata,
    CancellationToken cancellationToken = default)
{
    CallCount++;
    Email = email;
    Metadata = metadata;
    return Task.FromResult(Result);
}
```

- [ ] **Step 2: Run the focused tests to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~LoginApiTests
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~IdentityModelTests
```

Expected: compilation fails because `SessionMetadata` and the three refresh-token properties do not exist.

- [ ] **Step 3: Add the metadata contract and EF mapping**

Create `SessionMetadata.cs`:

```csharp
using System.Net;

namespace TempoDesk.Application.Authentication;

public sealed record SessionMetadata(
    string? UserAgent,
    IPAddress? IpAddress);
```

Add `using System.Net;` before the namespace in `RefreshToken.cs`, then add
these properties to `RefreshToken`:

```csharp
public string? DeviceName { get; set; }
public string? UserAgent { get; set; }
public IPAddress? IpAddress { get; set; }
```

Add these mappings in `ConfigureRefreshTokens`:

```csharp
entity.Property(token => token.DeviceName)
    .HasColumnName("device_name")
    .HasMaxLength(200);
entity.Property(token => token.UserAgent).HasColumnName("user_agent");
entity.Property(token => token.IpAddress)
    .HasColumnName("ip_address")
    .HasColumnType("inet");
```

- [ ] **Step 4: Pass bounded metadata through login and initial token issuance**

Change the application signatures to:

```csharp
Task<LoginResult> LoginAsync(
    string email,
    string password,
    SessionMetadata metadata,
    CancellationToken cancellationToken = default);

Task<AuthenticationTokenResult> IssueAsync(
    Guid userId,
    SessionMetadata metadata,
    CancellationToken cancellationToken = default);

Task CreateAsync(
    Guid userId,
    Guid familyId,
    NewRefreshToken token,
    SessionMetadata metadata,
    CancellationToken cancellationToken = default);
```

In `LoginController`, construct the bounded metadata and pass it to the login
service:

```csharp
var userAgent = Request.Headers.UserAgent.ToString();
var metadata = new SessionMetadata(
    string.IsNullOrWhiteSpace(userAgent)
        ? null
        : userAgent[..Math.Min(userAgent.Length, 1024)],
    HttpContext.Connection.RemoteIpAddress);
var result = await login.LoginAsync(
    request.Email, request.Password, metadata, cancellationToken);
```

Pass `metadata` from `AccountLoginService.LoginAsync` to
`tokens.IssueAsync`. Pass it from `AuthenticationTokenService.IssueAsync` to
`refreshTokens.CreateAsync`. Set the two persisted fields in `CreateAsync`:

```csharp
UserAgent = metadata.UserAgent,
IpAddress = metadata.IpAddress,
```

Update every service-level login caller to pass either its asserted metadata or
`new SessionMetadata(null, null)`. Update the fake
`IAuthenticationTokenService.IssueAsync` in `RefreshTokenApiTests` to accept the
new `SessionMetadata metadata` parameter. Confirm the complete caller set with:

```bash
rg -n "LoginAsync\(|IssueAsync\(" server/src server/tests -g '*.cs'
```

- [ ] **Step 5: Run API and model tests to verify GREEN**

Run the two Step 2 commands.

Expected: both commands pass with zero failures.

- [ ] **Step 6: Write failing persistence and rotation tests**

In `LoginIntegrationTests`, call login with:

```csharp
var metadata = new SessionMetadata(
    "TempoDesk Test Agent",
    IPAddress.Parse("203.0.113.10"));
var result = await login.LoginAsync(email, Password, metadata);
```

Query `user_agent`, `ip_address`, and `device_name`; assert the first two match
and `device_name` is null.

In `RefreshTokenIntegrationTests.ValidTokenRotatesOnceAndReturnsNewTokenPair`,
seed the initial login with the same metadata and select the metadata columns
for both rows:

```csharp
Assert.Equal("TempoDesk Test Agent", reader.GetString(userAgentOrdinal));
Assert.Equal("203.0.113.10", reader.GetFieldValue<IPAddress>(ipOrdinal).ToString());
Assert.True(reader.IsDBNull(deviceNameOrdinal));
```

- [ ] **Step 7: Run the integration tests to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter "FullyQualifiedName~LoginIntegrationTests|FullyQualifiedName~RefreshTokenIntegrationTests.ValidToken"
```

Expected: login persistence passes after Step 4, but rotation fails because the replacement row has null metadata.

- [ ] **Step 8: Preserve metadata during rotation**

When adding the replacement `RefreshToken` in `RotateAsync`, copy the locked
token's metadata:

```csharp
DeviceName = token.DeviceName,
UserAgent = token.UserAgent,
IpAddress = token.IpAddress,
```

- [ ] **Step 9: Run the focused integration tests to verify GREEN**

Run the Step 7 command.

Expected: all selected tests pass with zero failures.

- [ ] **Step 10: Commit the metadata slice**

```bash
git add server/src/TempoDesk.Application/Authentication server/src/TempoDesk.Api/Features/Authentication/Login server/src/TempoDesk.Infrastructure/Identity server/tests/TempoDesk.Api.Tests/Authentication/Login/LoginApiTests.cs server/tests/TempoDesk.IntegrationTests
git commit -m "feat(auth): capture login session metadata"
```

---

### Task 2: Add session service and repository operations

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/Session/AuthenticationSession.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Session/AuthenticationSessionStatus.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Session/IAuthenticationSessionService.cs`
- Modify: `server/src/TempoDesk.Application/Authentication/RefreshToken/IRefreshTokenRepository.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/Session/AuthenticationSessionService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshTokenRepository.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Modify: `server/tests/TempoDesk.IntegrationTests/Authentication/RefreshToken/RefreshTokenIntegrationTests.cs`

**Interfaces:**
- Consumes: refresh-token families and `TimeProvider`.
- Produces: `AuthenticationSession` records without token secrets.
- Produces: `IAuthenticationSessionService.LogoutAsync`, `ListAsync`, `RevokeAsync`, and `RevokeAllAsync`.
- Produces: repository operations whose ownership predicates include both `user_id` and `family_id`.

- [ ] **Step 1: Define the desired application contracts in failing integration tests**

Add these contracts to the test code through usages that do not yet compile:

```csharp
var sessions = scope.ServiceProvider
    .GetRequiredService<IAuthenticationSessionService>();

var listed = await sessions.ListAsync(userId);
Assert.Equal(AuthenticationSessionStatus.Active, listed[0].Status);

Assert.True(await sessions.RevokeAsync(userId, listed[0].Id));
Assert.True(await sessions.RevokeAsync(userId, listed[0].Id));
Assert.False(await sessions.RevokeAsync(otherUserId, listed[0].Id));

await sessions.LogoutAsync(rawRefreshToken);
await sessions.RevokeAllAsync(userId);
```

Add focused integration tests with these behaviors:

1. two logins for one user list as two sessions ordered by `LastUsedAt`;
2. another user's family never appears in that list;
3. revoking one owned family leaves the other family active;
4. repeated owned revocation returns true and preserves the first `revoked_at`;
5. foreign/missing family revocation returns false and does not alter rows;
6. logout revokes only the family containing the presented raw token, and a
   repeated or unknown token remains safe;
7. revoke-all revokes every family for the target user and no family for another
   user;
8. `auth.logout`, `session.revoke`, and `session.revoke_all` audit events exist
   without secret or metadata values.

- [ ] **Step 2: Run session integration tests to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~RefreshTokenIntegrationTests.Session
```

Expected: compilation fails because `IAuthenticationSessionService`, `AuthenticationSession`, and `AuthenticationSessionStatus` do not exist.

- [ ] **Step 3: Add session result types and service interface**

Create `AuthenticationSessionStatus.cs`:

```csharp
namespace TempoDesk.Application.Authentication;

public enum AuthenticationSessionStatus
{
    Active,
    Revoked,
    Expired
}
```

Create `AuthenticationSession.cs`:

```csharp
namespace TempoDesk.Application.Authentication;

public sealed record AuthenticationSession(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    AuthenticationSessionStatus Status);
```

Create `IAuthenticationSessionService.cs`:

```csharp
namespace TempoDesk.Application.Authentication;

public interface IAuthenticationSessionService
{
    Task LogoutAsync(
        string rawToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthenticationSession>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task RevokeAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Extend the repository contract**

Add these exact members to `IRefreshTokenRepository`:

```csharp
Task LogoutAsync(
    string? tokenHash,
    DateTimeOffset now,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<AuthenticationSession>> ListSessionsAsync(
    Guid userId,
    DateTimeOffset now,
    CancellationToken cancellationToken = default);

Task<bool> RevokeSessionAsync(
    Guid userId,
    Guid familyId,
    DateTimeOffset now,
    CancellationToken cancellationToken = default);

Task RevokeAllSessionsAsync(
    Guid userId,
    DateTimeOffset now,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement the minimal session service**

Create `AuthenticationSessionService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class AuthenticationSessionService(
    IRefreshTokenRepository refreshTokens,
    TimeProvider timeProvider) : IAuthenticationSessionService
{
    public Task LogoutAsync(
        string rawToken,
        CancellationToken cancellationToken = default) =>
        refreshTokens.LogoutAsync(
            string.IsNullOrEmpty(rawToken) || rawToken.Length > 512
                ? null
                : Hash(rawToken),
            timeProvider.GetUtcNow(),
            cancellationToken);

    public Task<IReadOnlyList<AuthenticationSession>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        refreshTokens.ListSessionsAsync(
            userId, timeProvider.GetUtcNow(), cancellationToken);

    public Task<bool> RevokeAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        refreshTokens.RevokeSessionAsync(
            userId, sessionId, timeProvider.GetUtcNow(), cancellationToken);

    public Task RevokeAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        refreshTokens.RevokeAllSessionsAsync(
            userId, timeProvider.GetUtcNow(), cancellationToken);

    private static string Hash(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
```

Register it in `IdentityConfiguration`:

```csharp
services.AddScoped<IAuthenticationSessionService,
    AuthenticationSessionService>();
```

- [ ] **Step 6: Implement listing without selecting token secrets**

In `ListSessionsAsync`, select only session-safe fields, materialize them, group
by family, and compute status with the supplied `now`:

```csharp
var rows = await context.RefreshTokens
    .AsNoTracking()
    .Where(token => token.UserId == userId)
    .Select(token => new
    {
        token.FamilyId,
        token.UserAgent,
        token.IpAddress,
        token.CreatedAt,
        token.UsedAt,
        token.ExpiresAt,
        token.RevokedAt
    })
    .ToListAsync(cancellationToken);

return rows
    .GroupBy(row => row.FamilyId)
    .Select(family =>
    {
        var createdAt = family.Min(row => row.CreatedAt);
        var active = family.Any(row =>
            row.RevokedAt is null && row.ExpiresAt > now);
        var allRevoked = family.All(row => row.RevokedAt is not null);
        return new AuthenticationSession(
            family.Key,
            family.Select(row => row.UserAgent).FirstOrDefault(value => value is not null),
            family.Select(row => row.IpAddress?.ToString())
                .FirstOrDefault(value => value is not null),
            createdAt,
            family.Where(row => row.UsedAt is not null)
                .Select(row => row.UsedAt!.Value)
                .DefaultIfEmpty(createdAt)
                .Max(),
            family.Max(row => row.ExpiresAt),
            active
                ? AuthenticationSessionStatus.Active
                : allRevoked
                    ? AuthenticationSessionStatus.Revoked
                    : AuthenticationSessionStatus.Expired);
    })
    .OrderByDescending(session => session.LastUsedAt)
    .ToArray();
```

- [ ] **Step 7: Implement transactional logout and revocations with audit**

For `LogoutAsync`, open a transaction, load the matching token by hash, update
only unrevoked rows in its family, and add:

```csharp
AddAudit("auth.logout", token is not null && changed > 0, token?.UserId, now);
```

For `RevokeSessionAsync`, first determine ownership using both keys:

```csharp
var owned = await context.RefreshTokens.AnyAsync(
    token => token.UserId == userId && token.FamilyId == familyId,
    cancellationToken);
```

If owned, update only rows with `RevokedAt == null`, audit
`session.revoke` as successful, commit, and return true. If not owned, audit it
as unsuccessful for the authenticated `userId`, commit, and return false. Do
not update already-revoked rows.

For `RevokeAllSessionsAsync`, update only unrevoked rows belonging to `userId`,
audit `session.revoke_all` as successful even when zero rows change, and commit.
Use `ExecuteUpdateAsync` with:

```csharp
.SetProperty(token => token.RevokedAt, now)
```

- [ ] **Step 8: Run session integration tests to verify GREEN**

Run the Step 2 command.

Expected: all session-focused integration tests pass with zero failures.

- [ ] **Step 9: Run all refresh-token integration tests**

Run:

```bash
dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~RefreshTokenIntegrationTests
```

Expected: session tests and all existing rotation/reuse/concurrency tests pass.

- [ ] **Step 10: Commit the service slice**

```bash
git add server/src/TempoDesk.Application/Authentication/Session server/src/TempoDesk.Application/Authentication/RefreshToken/IRefreshTokenRepository.cs server/src/TempoDesk.Infrastructure/Identity server/tests/TempoDesk.IntegrationTests/Authentication/RefreshToken/RefreshTokenIntegrationTests.cs
git commit -m "feat(auth): manage refresh token sessions"
```

---

### Task 3: Expose logout and authenticated session APIs

**Files:**
- Create: `server/src/TempoDesk.Api/Features/Authentication/Logout/LogoutController.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/Session/SessionResponseDto.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/Session/SessionsController.cs`
- Create: `server/tests/TempoDesk.Api.Tests/Authentication/Logout/LogoutApiTests.cs`
- Create: `server/tests/TempoDesk.Api.Tests/Authentication/Session/SessionApiTests.cs`

**Interfaces:**
- Consumes: `IAuthenticationSessionService` from Task 2.
- Produces: `POST /api/auth/logout`.
- Produces: authorized `GET /api/auth/sessions`, `DELETE /api/auth/sessions/{id}`, and `DELETE /api/auth/sessions`.
- Produces: lowercase JSON status values `active`, `revoked`, and `expired`.

- [ ] **Step 1: Write failing logout API tests**

Create `LogoutApiTests` with a fake `IAuthenticationSessionService`. Assert:

```csharp
using var request = new HttpRequestMessage(
    HttpMethod.Post, "/api/auth/logout");
request.Headers.Add(
    "Cookie", "tempodesk.refresh_token=current-refresh-token");
using var response = await client.SendAsync(request);

Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
Assert.Equal("current-refresh-token", service.RawToken);
var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
Assert.Contains("tempodesk.refresh_token=", cookie);
Assert.Contains("expires=Thu, 01 Jan 1970", cookie,
    StringComparison.OrdinalIgnoreCase);
```

Add a second test with no cookie and assert the same 204 response and expired
cookie.

- [ ] **Step 2: Run logout API tests to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~LogoutApiTests
```

Expected: tests fail with HTTP 404 because the route does not exist.

- [ ] **Step 3: Implement the logout controller**

Create `LogoutController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class LogoutController(
    IAuthenticationSessionService sessions) : ControllerBase
{
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        CancellationToken cancellationToken)
    {
        await sessions.LogoutAsync(
            RefreshTokenCookie.Read(Request), cancellationToken);
        RefreshTokenCookie.Delete(Response, Request.IsHttps);
        return NoContent();
    }
}
```

- [ ] **Step 4: Run logout API tests to verify GREEN**

Run the Step 2 command.

Expected: both logout tests pass.

- [ ] **Step 5: Write failing authenticated session API tests**

Create `SessionApiTests` with a test authentication handler that returns a
principal containing `ClaimTypes.NameIdentifier` only when a bearer header is
present. Replace `IAuthenticationSessionService` with a fake.

Test these exact cases:

- no bearer header returns 401 for all three session routes and never calls the
  service;
- `GET /api/auth/sessions` passes the principal's user ID and returns the exact
  JSON fields with lowercase status;
- owned `DELETE /api/auth/sessions/{id}` returns 204;
- fake service returning false for the same route returns 404;
- `DELETE /api/auth/sessions` passes the principal's user ID and returns 204.

Use this authentication registration in the factory:

```csharp
services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "Test";
        options.DefaultChallengeScheme = "Test";
    })
    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
        "Test", _ => { });
```

The handler creates this identity from the bearer value:

```csharp
if (!Request.Headers.Authorization.ToString().StartsWith(
        "Bearer ", StringComparison.Ordinal))
    return Task.FromResult(AuthenticateResult.NoResult());

var userId = Request.Headers.Authorization.ToString()["Bearer ".Length..];
var identity = new ClaimsIdentity(
    [new Claim(ClaimTypes.NameIdentifier, userId)],
    Scheme.Name);
return Task.FromResult(AuthenticateResult.Success(
    new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
```

- [ ] **Step 6: Run session API tests to verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~SessionApiTests
```

Expected: authenticated requests fail with HTTP 404 because the routes do not exist.

- [ ] **Step 7: Add the DTO and authorized controller**

Create `SessionResponseDto.cs`:

```csharp
namespace TempoDesk.Api.Controllers;

public sealed record SessionResponseDto(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string Status);
```

Create `SessionsController.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TempoDesk.Application.Authentication;

namespace TempoDesk.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/auth/sessions")]
public sealed class SessionsController(
    IAuthenticationSessionService sessions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        CancellationToken cancellationToken)
    {
        var result = await sessions.ListAsync(UserId(), cancellationToken);
        return Ok(result.Select(session => new SessionResponseDto(
            session.Id,
            session.UserAgent,
            session.IpAddress,
            session.CreatedAt,
            session.LastUsedAt,
            session.ExpiresAt,
            session.Status.ToString().ToLowerInvariant())));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(
        Guid id,
        CancellationToken cancellationToken) =>
        await sessions.RevokeAsync(UserId(), id, cancellationToken)
            ? NoContent()
            : NotFound();

    [HttpDelete]
    public async Task<IActionResult> RevokeAll(
        CancellationToken cancellationToken)
    {
        await sessions.RevokeAllAsync(UserId(), cancellationToken);
        return NoContent();
    }

    private Guid UserId() => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
```

- [ ] **Step 8: Run all new API tests to verify GREEN**

Run:

```bash
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter "FullyQualifiedName~LogoutApiTests|FullyQualifiedName~SessionApiTests"
```

Expected: all logout/session API tests pass with zero failures.

- [ ] **Step 9: Run all authentication API tests**

Run:

```bash
dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj
```

Expected: the complete API test project, including login, registration,
refresh, logout, and session tests, passes.

- [ ] **Step 10: Commit the API slice**

```bash
git add server/src/TempoDesk.Api/Features/Authentication server/tests/TempoDesk.Api.Tests/Authentication
git commit -m "feat(api): expose logout and session management"
```

---

### Task 4: Verify SCRUM-25 end to end

**Files:**
- Verify only; modify a previously listed file only if a verification command exposes a defect.

**Interfaces:**
- Consumes: all Tasks 1-3.
- Produces: fresh evidence that the solution builds, tests pass, formatting is valid, and no secret-bearing field entered an API or audit contract.

- [ ] **Step 1: Run the complete server test suite**

Run:

```bash
dotnet test server/TempoDesk.sln
```

Expected: every server test passes with zero failures.

- [ ] **Step 2: Run formatting verification**

Run:

```bash
dotnet format server/TempoDesk.sln --verify-no-changes
```

Expected: exit code 0 with no files requiring formatting.

- [ ] **Step 3: Build the server in Release mode**

Run:

```bash
dotnet build server/TempoDesk.sln --configuration Release --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 4: Inspect the final diff and secret boundaries**

Run:

```bash
git diff development...HEAD --check
rg -n "TokenHash|RefreshToken|Authorization|Cookie" server/src/TempoDesk.Api/Features/Authentication/Session server/src/TempoDesk.Application/Authentication/Session
git status --short
```

Expected: `git diff --check` emits nothing; the search finds no DTO property or
session result exposing a token/hash/header/cookie; status contains only
intentional files, if any.

- [ ] **Step 5: Commit any verification-only correction**

If Steps 1-4 required a correction, stage only the corrected files and commit:

```bash
git commit -m "fix(auth): complete SCRUM-25 verification"
```

If no correction was required, do not create an empty commit.
