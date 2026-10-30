# SCRUM-22 Email/password JWT Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let verified, unlocked Identity accounts exchange an email and password for a short-lived bearer JWT.

**Architecture:** Add a small application login contract, implement it beside the existing Identity registration service, and expose it through `POST /api/auth/login`. Reuse Identity's email normalization, confirmed-email policy, password verification, failed-attempt tracking, and lockout; configure the same issuer, audience, signing key, and lifetime for token issuance and JWT bearer validation.

**Tech Stack:** .NET 10, ASP.NET Core Identity, JWT bearer authentication, `System.IdentityModel.Tokens.Jwt`, EF Core/PostgreSQL, xUnit, Testcontainers.

## Global Constraints

- JWTs use HMAC-SHA256 and contain only explicit `sub`, `jti`, and `iat` account claims.
- Unknown email, wrong password, unverified email, and lockout return the same HTTP 401 contract.
- Never log or persist submitted passwords, raw JWTs, or signing keys.
- Audit every login attempt as `user.login`, with user ID only when a matching account exists.
- Access-token lifetime is configurable and short; default deployment value is 15 minutes.
- Do not add refresh tokens, logout/revocation, roles, permissions, client UI, or a database migration.

---

### Task 1: JWT configuration and bearer validation

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj`
- Modify: `server/src/TempoDesk.Infrastructure/Configuration/FoundationOptions.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Modify: `server/src/TempoDesk.Api/Program.cs`
- Modify: `server/tests/TempoDesk.Application.Tests/ConfigurationTests.cs`
- Modify: `.env.example`
- Modify: `docker-compose.yml`

**Interfaces:**
- Produces: `AuthenticationOptions` properties `Enabled`, `Issuer`, `Audience`, `SigningKey`, and `AccessTokenMinutes`.
- Produces: JWT bearer authentication registered under `JwtBearerDefaults.AuthenticationScheme`.
- Consumes: no new application types.

- [ ] **Step 1: Write failing configuration tests**

Add cases proving enabled authentication rejects a missing issuer/audience/key, a key shorter than 32 characters, and lifetimes outside `1..60`, plus one valid case that reads all values. Assert validation exceptions do not contain the supplied signing key.

```csharp
[Theory]
[InlineData("Authentication:Issuer", "")]
[InlineData("Authentication:Audience", "")]
[InlineData("Authentication:SigningKey", "short")]
[InlineData("Authentication:AccessTokenMinutes", "0")]
[InlineData("Authentication:AccessTokenMinutes", "61")]
public void AuthenticationRejectsInvalidJwtSettings(string key, string value)
{
    const string secret = "0123456789abcdef0123456789abcdef";
    using var provider = Create(new()
    {
        ["Database:Password"] = "private-password",
        ["Authentication:Enabled"] = "true",
        ["Authentication:Issuer"] = "https://api.tempodesk.local",
        ["Authentication:Audience"] = "tempodesk-api",
        ["Authentication:SigningKey"] = secret,
        ["Authentication:AccessTokenMinutes"] = "15",
        [key] = value
    });

    var exception = Assert.Throws<OptionsValidationException>(() =>
        _ = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value);
    Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Verify the focused tests fail for the missing properties/validation**

Run: `dotnet test server/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj --filter FullyQualifiedName~ConfigurationTests`

Expected: FAIL because the current options still expose `Authority` and do not validate JWT signing settings.

- [ ] **Step 3: Implement the smallest configuration change**

Replace `Authority` with the JWT fields and validate only when enabled:

```csharp
public sealed class AuthenticationOptions
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
}
```

Validation must require absolute HTTP(S) issuer, nonblank audience, a signing key of at least 32 UTF-8 bytes, and `AccessTokenMinutes is >= 1 and <= 60`.

Add `Microsoft.AspNetCore.Authentication.JwtBearer` version `10.0.10` to `TempoDesk.Infrastructure.csproj`; the shared framework does not contain the JWT bearer/token assemblies used by configuration and issuance.

Register JWT bearer authentication in `AddTempoDeskIdentity` using `IssuerSigningKey`, `ValidIssuer`, `ValidAudience`, `ValidateIssuerSigningKey`, `ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, and `ClockSkew = TimeSpan.Zero`. Add `app.UseAuthentication()` and `app.UseAuthorization()` after CORS and before controller mapping.

- [ ] **Step 4: Wire deployment configuration without committing a secret**

Add `JWT_SIGNING_KEY=replace_with_at_least_32_random_characters` to `.env.example`. In the API service add:

```yaml
Authentication__Enabled: "true"
Authentication__Issuer: http://localhost:${API_PORT:-5023}
Authentication__Audience: tempodesk-api
Authentication__SigningKey: ${JWT_SIGNING_KEY:?Set JWT_SIGNING_KEY in .env}
Authentication__AccessTokenMinutes: "15"
```

- [ ] **Step 5: Run focused tests and build**

Run: `dotnet test server/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj --filter FullyQualifiedName~ConfigurationTests`

Expected: PASS.

Run: `dotnet build server/TempoDesk.sln --no-restore`

Expected: exit 0.

- [ ] **Step 6: Commit**

```bash
git add .env.example docker-compose.yml server/src/TempoDesk.Infrastructure/TempoDesk.Infrastructure.csproj server/src/TempoDesk.Infrastructure/Configuration/FoundationOptions.cs server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs server/src/TempoDesk.Api/Program.cs server/tests/TempoDesk.Application.Tests/ConfigurationTests.cs
git commit -m "feat(auth): configure JWT bearer authentication"
```

### Task 2: Identity login, token issuance, lockout, and audit

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/LoginContracts.cs`
- Create: `server/src/TempoDesk.Infrastructure/Identity/AccountLoginService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Create: `server/tests/TempoDesk.IntegrationTests/LoginIntegrationTests.cs`

**Interfaces:**
- Produces: `IAccountLoginService.LoginAsync(string email, string password, CancellationToken cancellationToken = default)`.
- Produces: `LoginResult` with nullable `AccessToken`, integer `ExpiresIn`, and `Succeeded` derived from token presence.
- Consumes: `AuthenticationOptions` from Task 1 and existing `ApplicationUser`, `SignInManager<ApplicationUser>`, `TempoDeskIdentityDbContext`, `SecurityAuditLog`, and `TimeProvider`.

- [ ] **Step 1: Write the failing successful-login integration test**

Create a PostgreSQL Testcontainer using the same two migrations and runtime-role setup as `RegistrationIntegrationTests`. Configure authentication with a fixed 32+ character test key and manual clock, create a confirmed user, then assert:

```csharp
var result = await login.LoginAsync("PERSON@example.com", password);
Assert.True(result.Succeeded);
Assert.Equal(900, result.ExpiresIn);

var handler = new JwtSecurityTokenHandler();
var token = handler.ReadJwtToken(result.AccessToken);
Assert.Equal("https://issuer.example", token.Issuer);
Assert.Equal("tempodesk-api", Assert.Single(token.Audiences));
Assert.Equal(user.Id.ToString(), token.Subject);
Assert.Equal(clock.GetUtcNow().AddMinutes(15).UtcDateTime, token.ValidTo);
Assert.DoesNotContain(token.Claims, claim =>
    claim.Type.Contains("email", StringComparison.OrdinalIgnoreCase) ||
    claim.Value == password);
```

Also validate the raw token with `TokenValidationParameters` matching runtime configuration, including lifetime and zero clock skew.

- [ ] **Step 2: Verify the successful-login test fails**

Run: `dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~LoginIntegrationTests.SuccessfulLogin`

Expected: FAIL because `IAccountLoginService` and the implementation do not exist.

- [ ] **Step 3: Add the minimal application contract and implementation**

Add:

```csharp
public interface IAccountLoginService
{
    Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record LoginResult(string? AccessToken, int ExpiresIn)
{
    public bool Succeeded => AccessToken is not null;
    public static LoginResult Rejected { get; } = new(null, 0);
}
```

`AccountLoginService` must:

1. Find the user with `UserManager.FindByEmailAsync` so Identity normalizes email.
2. Reject unknown users with a failed audit row.
3. Call `SignInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)`.
4. Reject `IsNotAllowed`, `IsLockedOut`, or bad password identically and audit failure.
5. On success create a `JwtSecurityToken` signed with `SecurityAlgorithms.HmacSha256`, using the configured issuer/audience, the manual clock for `nbf`/`exp`, and explicit `sub`, `jti`, and numeric-date `iat` claims.
6. Add a successful `user.login` audit row, save once, and return the encoded token plus lifetime seconds.

Register `IAccountLoginService` as scoped beside registration.

- [ ] **Step 4: Run the successful-login test**

Run: `dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~LoginIntegrationTests.SuccessfulLogin`

Expected: PASS.

- [ ] **Step 5: Write failing rejection, lockout, and audit integration tests**

Using the same container/provider, verify:

```csharp
Assert.False((await login.LoginAsync("missing@example.com", password)).Succeeded);
Assert.False((await login.LoginAsync(unconfirmed.Email!, password)).Succeeded);

for (var attempt = 0; attempt < 5; attempt++)
    Assert.False((await login.LoginAsync(confirmed.Email!, "Wrong-Password-7!")).Succeeded);
Assert.True(await users.IsLockedOutAsync(confirmed));
Assert.False((await login.LoginAsync(confirmed.Email!, password)).Succeeded);
```

Query `security_audit_logs` and assert one `user.login` row per attempt, success flags match outcomes, unknown email has null `user_id`, and no stored metadata/text contains password or token.

- [ ] **Step 6: Verify the new tests fail, then make only the required correction**

Run: `dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~LoginIntegrationTests`

Expected before correction: at least one new assertion FAIL for incomplete rejection/audit behavior.

Adjust only `AccountLoginService` until all cases pass; do not add new result variants that could expose account state.

- [ ] **Step 7: Run all login integration tests**

Run: `dotnet test server/tests/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter FullyQualifiedName~LoginIntegrationTests`

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add server/src/TempoDesk.Application/Authentication/LoginContracts.cs server/src/TempoDesk.Infrastructure/Identity/AccountLoginService.cs server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs server/tests/TempoDesk.IntegrationTests/LoginIntegrationTests.cs
git commit -m "feat(auth): issue access tokens for verified accounts"
```

### Task 3: Login HTTP contract

**Files:**
- Create: `server/src/TempoDesk.Api/Controllers/LoginController.cs`
- Create: `server/tests/TempoDesk.Api.Tests/LoginApiTests.cs`

**Interfaces:**
- Consumes: `IAccountLoginService` and `LoginResult` from Task 2.
- Produces: `POST /api/auth/login` accepting `LoginRequest` and returning `LoginResponse` or generic HTTP 401 ProblemDetails.

- [ ] **Step 1: Write failing API success test**

Replace `IAccountLoginService` in `FoundationFactory` with a fake returning `new LoginResult("header.payload.signature", 900)`, post a valid request, and assert exact status/body:

```csharp
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
Assert.Equal(
    "{\"accessToken\":\"header.payload.signature\",\"tokenType\":\"Bearer\",\"expiresIn\":900}",
    await response.Content.ReadAsStringAsync());
```

- [ ] **Step 2: Verify the API test fails**

Run: `dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~LoginApiTests`

Expected: FAIL with 404 because the route does not exist.

- [ ] **Step 3: Add the minimal controller**

Create an `[ApiController]` at `[Route("api/auth")]` with `POST login`. Validate email as required/email/max 256 and password as required/max 1024. On rejection return `Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized")`; on success return `Ok(new LoginResponse(result.AccessToken!, "Bearer", result.ExpiresIn))`.

```csharp
public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn);
```

- [ ] **Step 4: Run success test to green**

Run: `dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~LoginApiTests`

Expected: success test PASS.

- [ ] **Step 5: Write failing safe-failure and boundary tests**

Make the fake return `LoginResult.Rejected` for each conceptual failure and assert the responses are byte-for-byte equal HTTP 401 ProblemDetails documents after removing request-specific `traceId`. Also assert malformed email and missing password return HTTP 400 without invoking the service, and no response contains submitted email/password.

- [ ] **Step 6: Verify RED, make the controller correction, and verify GREEN**

Run: `dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter FullyQualifiedName~LoginApiTests`

Expected before correction: new boundary assertion FAIL.

Make the smallest controller/DTO correction, then rerun the same command.

Expected after correction: PASS.

- [ ] **Step 7: Commit**

```bash
git add server/src/TempoDesk.Api/Controllers/LoginController.cs server/tests/TempoDesk.Api.Tests/LoginApiTests.cs
git commit -m "feat(auth): expose email password login endpoint"
```

### Task 4: Full verification

**Files:**
- Verify only; modify a prior task's files only if a check exposes a defect.

**Interfaces:**
- Consumes: all deliverables from Tasks 1-3.
- Produces: verified SCRUM-22 implementation.

- [ ] **Step 1: Run formatting verification**

Run: `dotnet format server/TempoDesk.sln --verify-no-changes --no-restore`

Expected: exit 0 with no files requiring formatting.

- [ ] **Step 2: Run the full server test suite**

Run: `dotnet test server/TempoDesk.sln --no-restore`

Expected: all tests PASS, including Testcontainers integration tests.

- [ ] **Step 3: Run the release build**

Run: `dotnet build server/TempoDesk.sln -c Release --no-restore`

Expected: exit 0 with zero errors.

- [ ] **Step 4: Inspect secrets and scope**

Run: `rg -n "SigningKey|JWT_SIGNING_KEY|user.login|AccessToken" .env.example docker-compose.yml server/src server/tests`

Expected: no real secret, password, or raw token literal outside test-only fixed values; no refresh-token, role, or UI implementation.

Run: `git diff --check && git status --short`

Expected: no whitespace errors and only intended SCRUM-22 files changed.
