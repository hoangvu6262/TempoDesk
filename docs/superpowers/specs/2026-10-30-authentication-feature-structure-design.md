# Authentication feature structure design

## Goal

Reorganize Login, Registration, and authentication email code into feature-oriented folders inside the existing API, Application, and Infrastructure layers. Split types into clearly named files so DTOs, service contracts, service implementations, models, templates, and future repositories are easy to distinguish.

This is a structural refactor only. Routes, request/response JSON, validation, Identity behavior, JWT behavior, email delivery, database schema, and dependency direction must remain unchanged.

## Layer and feature boundaries

### API

Place HTTP-only types under `TempoDesk.Api/Features/Authentication`:

- `Login/Dtos/LoginRequestDto.cs`
- `Login/Dtos/LoginResponseDto.cs`
- `Login/LoginController.cs`
- `Registration/Dtos/RegisterRequestDto.cs`
- `Registration/Dtos/VerifyEmailRequestDto.cs`
- `Registration/Dtos/ResendVerificationRequestDto.cs`
- `Registration/RegistrationController.cs`

DTO names use the `Dto` suffix. Controllers keep their existing routes and actions.

### Application

Place use-case contracts and transport-independent models under `TempoDesk.Application/Authentication`:

- `Login/Models/LoginResult.cs`
- `Login/Services/IAccountLoginService.cs`
- `Registration/Models/RegistrationResult.cs`
- `Registration/Services/IAccountRegistrationService.cs`
- `Email/Models/EmailContent.cs`
- `Email/Models/VerificationEmail.cs`
- `Email/Services/IEmailVerificationSender.cs`
- `Email/Templates/AuthenticationEmailTemplates.cs`

Application models do not use the `Dto` suffix because they are not HTTP contracts.

### Infrastructure

Place framework-dependent implementations under their feature while retaining the Infrastructure layer:

- `Identity/Login/Services/AccountLoginService.cs`
- `Identity/Registration/Services/AccountRegistrationService.cs`
- `Email/Authentication/Services/SmtpEmailVerificationSender.cs`

Identity entities and `TempoDeskIdentityDbContext` remain where they are because moving them is unrelated to distinguishing Login, Registration, and Email responsibilities.

## Repository convention

Do not create an empty repository folder or wrappers around `UserManager`, `SignInManager`, and Identity stores. They already provide the data-access abstraction used by these features.

When a real TempoDesk repository is needed, place its interface in Application and implementation in Infrastructure:

- `Application/Authentication/<Feature>/Repositories/I<Name>Repository.cs`
- `Infrastructure/Identity/<Feature>/Repositories/Identity<Name>Repository.cs`

Repositories only handle persistence. They do not issue JWTs, choose HTTP responses, or implement use-case orchestration.

## Tests

Move authentication tests to matching feature folders without changing their namespaces or assertions:

- API: `Authentication/Login` and `Authentication/Registration`
- Application: `Authentication/Email`
- Integration: `Authentication/Login` and `Authentication/Registration`

Run the full server test suite and Release build after moves. Because this refactor is behavior-preserving, no new behavioral tests are required; existing tests are the regression contract.

## Persistent convention

Add a concise project rule that records:

- Feature folders live inside architectural layers.
- HTTP DTOs use the `Dto` suffix and remain in API.
- Service interfaces live in Application; framework-dependent implementations live in Infrastructure.
- Repository interfaces live in Application and implementations in Infrastructure, but repositories are created only when they add a real persistence boundary.
- Each public type has its own file, except tightly coupled private test helpers.

## Out of scope

- Splitting Registration and Email Verification into separate runtime services.
- Adding repository wrappers around ASP.NET Identity.
- Moving Identity entities or the DbContext.
- Changing namespaces, public routes, JSON property names, validation rules, DI lifetimes, or database behavior.
