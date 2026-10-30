# Authentication Feature Structure Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize Login, Registration, and authentication email code into feature folders with one clearly named public type per file, without changing runtime behavior.

**Architecture:** Keep API, Application, and Infrastructure as the architectural boundaries, and create feature folders inside each project. Preserve existing namespaces and dependency injection so the refactor is path/type-name focused; existing tests remain the behavior contract.

**Tech Stack:** .NET 10, ASP.NET Core MVC and Identity, xUnit, SDK-style project globbing, Cursor/Codex `.mdc` project rules.

## Global Constraints

- Preserve all routes, JSON property names, validation attributes, status codes, Identity/JWT behavior, SMTP behavior, DI lifetimes, and database behavior.
- HTTP request/response types use the `Dto` suffix and live in API feature folders.
- Application service interfaces, results, email models, and templates each live in their own file.
- Framework-dependent service implementations remain in Infrastructure.
- Do not add repository wrappers around `UserManager`, `SignInManager`, or Identity stores.
- Do not move Identity entities or `TempoDeskIdentityDbContext`.
- Preserve current namespaces to keep the diff structural and avoid unrelated caller edits.

---

### Task 1: API feature folders and DTO files

**Files:**
- Move: `server/src/TempoDesk.Api/Controllers/LoginController.cs` → `server/src/TempoDesk.Api/Features/Authentication/Login/LoginController.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/Login/Dtos/LoginRequestDto.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/Login/Dtos/LoginResponseDto.cs`
- Move: `server/src/TempoDesk.Api/Controllers/RegistrationController.cs` → `server/src/TempoDesk.Api/Features/Authentication/Registration/RegistrationController.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/Registration/Dtos/RegisterRequestDto.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/Registration/Dtos/VerifyEmailRequestDto.cs`
- Create: `server/src/TempoDesk.Api/Features/Authentication/Registration/Dtos/ResendVerificationRequestDto.cs`
- Test: `server/tests/TempoDesk.Api.Tests/LoginApiTests.cs`
- Test: `server/tests/TempoDesk.Api.Tests/RegistrationApiTests.cs`

**Interfaces:**
- Produces: `LoginRequestDto`, `LoginResponseDto`, `RegisterRequestDto`, `VerifyEmailRequestDto`, and `ResendVerificationRequestDto` in namespace `TempoDesk.Api.Controllers`.
- Preserves: `POST /api/auth/login`, `register`, `verify-email`, and `resend-verification`.

- [ ] **Step 1: Verify the existing API regression baseline**

Run: `dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter "FullyQualifiedName~LoginApiTests|FullyQualifiedName~RegistrationApiTests"`

Expected: all Login and Registration API tests PASS.

- [ ] **Step 2: Move controllers and split DTOs**

Move each controller with `apply_patch` while retaining its namespace and action bodies. Remove DTO declarations from controller files. Rename controller parameters and response construction to the new types:

```csharp
public async Task<IActionResult> Login(
    LoginRequestDto request,
    CancellationToken cancellationToken)

return Ok(new LoginResponseDto(
    result.AccessToken!, "Bearer", result.ExpiresIn));
```

```csharp
public async Task<IActionResult> Register(
    RegisterRequestDto request,
    CancellationToken cancellationToken)

public async Task<IActionResult> VerifyEmail(
    VerifyEmailRequestDto request,
    CancellationToken cancellationToken)

public async Task<IActionResult> ResendVerification(
    ResendVerificationRequestDto request,
    CancellationToken cancellationToken)
```

Create the DTO files with the unchanged validation contracts:

```csharp
namespace TempoDesk.Api.Controllers;

public sealed class LoginRequestDto
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = "";

    [Required, MaxLength(1024)]
    public string Password { get; init; } = "";
}
```

```csharp
namespace TempoDesk.Api.Controllers;

public sealed record LoginResponseDto(
    string AccessToken,
    string TokenType,
    int ExpiresIn);
```

Registration DTOs retain `[Required, EmailAddress, MaxLength(256)]`, password `[Required, MinLength(12), MaxLength(1024)]`, and token `[Required, MaxLength(512)]` exactly as currently declared.

- [ ] **Step 3: Run API tests after the move**

Run: `dotnet test server/tests/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter "FullyQualifiedName~LoginApiTests|FullyQualifiedName~RegistrationApiTests"`

Expected: PASS with the same test count and contracts.

- [ ] **Step 4: Commit the API structure**

```bash
git add -A server/src/TempoDesk.Api
git commit -m "refactor(auth): organize API features and DTOs"
```

### Task 2: Application contracts, models, and email templates

**Files:**
- Delete: `server/src/TempoDesk.Application/Authentication/LoginContracts.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Login/Services/IAccountLoginService.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Login/Models/LoginResult.cs`
- Delete: `server/src/TempoDesk.Application/Authentication/RegistrationContracts.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Registration/Services/IAccountRegistrationService.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Registration/Models/RegistrationResult.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Email/Models/VerificationEmail.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Email/Services/IEmailVerificationSender.cs`
- Delete: `server/src/TempoDesk.Application/Authentication/AuthenticationEmailTemplates.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Email/Models/EmailContent.cs`
- Create: `server/src/TempoDesk.Application/Authentication/Email/Templates/AuthenticationEmailTemplates.cs`
- Test: `server/tests/TempoDesk.Application.Tests/AuthenticationEmailTemplatesTests.cs`
- Test: `server/tests/TempoDesk.Application.Tests/EmailDeliveryTests.cs`

**Interfaces:**
- Preserves all existing public signatures and namespace `TempoDesk.Application.Authentication`.
- Splits each public interface/record/static class into a matching file without logic changes.

- [ ] **Step 1: Verify the existing Application regression baseline**

Run: `dotnet test server/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj --filter "FullyQualifiedName~AuthenticationEmailTemplatesTests|FullyQualifiedName~EmailDeliveryTests"`

Expected: all email template and delivery tests PASS.

- [ ] **Step 2: Split login and registration contracts**

Move these declarations unchanged into individual files:

```csharp
public interface IAccountLoginService
{
    Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);
}
```

```csharp
public sealed record LoginResult(string? AccessToken, int ExpiresIn)
{
    public bool Succeeded => AccessToken is not null;
    public static LoginResult Rejected { get; } = new(null, 0);
}
```

```csharp
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
```

Keep `RegistrationResult` behavior, `VerificationEmail` positional properties, and `IEmailVerificationSender.SendAsync` signature byte-for-byte equivalent to their current declarations.

- [ ] **Step 3: Split email model and template files**

Move `EmailContent` to its model file and move `AuthenticationEmailTemplates` unchanged to `Email/Templates`. Keep both `Verification` and `PasswordReset` templates and all escaping/validation logic unchanged.

- [ ] **Step 4: Run Application tests and compile callers**

Run: `dotnet test server/tests/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj --filter "FullyQualifiedName~AuthenticationEmailTemplatesTests|FullyQualifiedName~EmailDeliveryTests"`

Expected: PASS.

Run: `dotnet build server/TempoDesk.sln --no-restore`

Expected: build succeeds with 0 errors.

- [ ] **Step 5: Commit the Application structure**

```bash
git add server/src/TempoDesk.Application/Authentication
git commit -m "refactor(auth): split application contracts by feature"
```

### Task 3: Infrastructure implementations and test layout

**Files:**
- Move: `server/src/TempoDesk.Infrastructure/Identity/AccountLoginService.cs` → `server/src/TempoDesk.Infrastructure/Identity/Login/Services/AccountLoginService.cs`
- Move: `server/src/TempoDesk.Infrastructure/Identity/AccountRegistrationService.cs` → `server/src/TempoDesk.Infrastructure/Identity/Registration/Services/AccountRegistrationService.cs`
- Move: `server/src/TempoDesk.Infrastructure/Email/SmtpEmailVerificationSender.cs` → `server/src/TempoDesk.Infrastructure/Email/Authentication/Services/SmtpEmailVerificationSender.cs`
- Move: `server/tests/TempoDesk.Api.Tests/LoginApiTests.cs` → `server/tests/TempoDesk.Api.Tests/Authentication/Login/LoginApiTests.cs`
- Move: `server/tests/TempoDesk.Api.Tests/RegistrationApiTests.cs` → `server/tests/TempoDesk.Api.Tests/Authentication/Registration/RegistrationApiTests.cs`
- Move: `server/tests/TempoDesk.Application.Tests/AuthenticationEmailTemplatesTests.cs` → `server/tests/TempoDesk.Application.Tests/Authentication/Email/AuthenticationEmailTemplatesTests.cs`
- Move: `server/tests/TempoDesk.Application.Tests/EmailDeliveryTests.cs` → `server/tests/TempoDesk.Application.Tests/Authentication/Email/EmailDeliveryTests.cs`
- Move: `server/tests/TempoDesk.IntegrationTests/LoginIntegrationTests.cs` → `server/tests/TempoDesk.IntegrationTests/Authentication/Login/LoginIntegrationTests.cs`
- Move: `server/tests/TempoDesk.IntegrationTests/RegistrationIntegrationTests.cs` → `server/tests/TempoDesk.IntegrationTests/Authentication/Registration/RegistrationIntegrationTests.cs`

**Interfaces:**
- Preserves all namespaces, accessibility, class names, DI registrations, and test assertions.
- Uses SDK project globbing; no `.csproj` include entries are added.

- [ ] **Step 1: Move implementation files without editing their contents**

Use `apply_patch` file moves. Do not change namespaces or DI registration because type identity remains unchanged.

- [ ] **Step 2: Move feature tests without editing their contents**

Use `apply_patch` file moves into matching Authentication feature folders. Keep namespaces unchanged so test discovery and helper visibility remain identical.

- [ ] **Step 3: Run focused feature tests**

Run: `dotnet test server/TempoDesk.Api.Tests/TempoDesk.Api.Tests.csproj --filter "FullyQualifiedName~LoginApiTests|FullyQualifiedName~RegistrationApiTests"`

Run: `dotnet test server/TempoDesk.Application.Tests/TempoDesk.Application.Tests.csproj --filter "FullyQualifiedName~AuthenticationEmailTemplatesTests|FullyQualifiedName~EmailDeliveryTests"`

Run: `dotnet test server/TempoDesk.IntegrationTests/TempoDesk.IntegrationTests.csproj --filter "FullyQualifiedName~LoginIntegrationTests|FullyQualifiedName~RegistrationIntegrationTests"`

Expected: all commands PASS with unchanged counts.

- [ ] **Step 4: Commit implementation and test moves**

```bash
git add server/src/TempoDesk.Infrastructure server/tests
git commit -m "refactor(auth): align implementations and tests by feature"
```

### Task 4: Persistent project convention and full verification

**Files:**
- Create: `.cursor/rules/authentication-feature-structure.mdc`
- Verify: all files moved in Tasks 1-3.

**Interfaces:**
- Produces: an always-applied project rule for feature folders, DTO suffixes, service placement, repository placement, and one-public-type-per-file.

- [ ] **Step 1: Add the project rule**

Create:

```markdown
---
description: Authentication feature folder and type naming conventions
alwaysApply: true
---

# Authentication Structure

- Organize authentication code by feature inside the existing API, Application, and Infrastructure layers.
- Keep HTTP DTOs in API feature `Dtos/` folders and suffix their names with `Dto`.
- Keep service interfaces in Application `Services/` folders.
- Keep framework-dependent service implementations in Infrastructure `Services/` folders.
- Put repository interfaces in Application and implementations in Infrastructure only when they add a real persistence boundary; do not wrap framework stores without a concrete need.
- Give each public type its own file, except tightly coupled private test helpers.
```

- [ ] **Step 2: Verify file naming and no obsolete files**

Run: `rg --files server/src server/tests | sort`

Expected: all authentication feature files appear only at the approved paths; `Controllers/LoginController.cs`, `Controllers/RegistrationController.cs`, `LoginContracts.cs`, `RegistrationContracts.cs`, and the root `AuthenticationEmailTemplates.cs` are absent.

- [ ] **Step 3: Run format verification on changed C# files**

Run `dotnet format server/TempoDesk.sln --verify-no-changes --no-restore --include` with every moved or created C# file.

Expected: exit 0. Do not reformat unrelated files that already fail the repository-wide whitespace check.

- [ ] **Step 4: Run the full server test suite**

Run: `dotnet test server/TempoDesk.sln --no-restore`

Expected: all tests PASS with no failures.

- [ ] **Step 5: Run the Release build**

Run: `dotnet build server/TempoDesk.sln -c Release --no-restore`

Expected: build succeeds with 0 warnings and 0 errors.

- [ ] **Step 6: Check scope and commit the convention**

Run: `git diff --check && git status --short`

Expected: no whitespace errors and only `.cursor/rules/authentication-feature-structure.mdc` remains uncommitted.

```bash
git add .cursor/rules/authentication-feature-structure.mdc
git commit -m "docs: define authentication structure convention"
```
