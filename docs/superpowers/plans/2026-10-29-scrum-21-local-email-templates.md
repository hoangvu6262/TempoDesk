# SCRUM-21 Local Email Service and Authentication Templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver registration verification emails to a local Mailpit inbox and add verified HTML/text templates for email verification and password reset.

**Architecture:** Keep the existing `IEmailVerificationSender` application boundary. Add framework-free template rendering in Application, implement that boundary with the platform SMTP client in Infrastructure, and configure the local API to send to a pinned Mailpit container using a configured frontend base URL.

**Tech Stack:** .NET 10, xUnit, `System.Net.Mail`, ASP.NET Core options/DI, Docker Compose, Mailpit `v1.32.0`.

## Global Constraints

- Do not stage, force-add, or commit any file under `/docs`.
- Do not add a NuGet dependency; use `System.Net.Mail` from the platform.
- Keep `IEmailVerificationSender.SendAsync(VerificationEmail, CancellationToken)` unchanged so registration callers do not break.
- Build links from `EmailOptions.FrontendBaseUrl`; never hard-code a deployment host in templates.
- Verification uses `/verify-email?token=...`; password reset uses `/reset-password?token=...`.
- Do not implement password-reset token generation, APIs, frontend screens, provider authentication, TLS options, retries, queues, or delivery tracking.
- Never log or persist raw verification/reset tokens or passwords.

---

### Task 1: Authentication email templates

**Files:**
- Create: `server/src/TempoDesk.Application/Authentication/AuthenticationEmailTemplates.cs`
- Create: `server/tests/TempoDesk.Application.Tests/AuthenticationEmailTemplatesTests.cs`

**Interfaces:**
- Consumes: `System.Uri`, opaque token strings.
- Produces: `EmailContent(string Subject, string TextBody, string HtmlBody)`.
- Produces: `AuthenticationEmailTemplates.Verification(Uri frontendBaseUrl, string token)`.
- Produces: `AuthenticationEmailTemplates.PasswordReset(Uri frontendBaseUrl, string token)`.

- [ ] **Step 1: Create the feature branch from the completed SCRUM-20 commit**

```bash
git switch -c sprint-1/feat/SCRUM-21
```

Expected: current branch is `sprint-1/feat/SCRUM-21`, based on `02ce715`.

- [ ] **Step 2: Write failing template tests**

Create `server/tests/TempoDesk.Application.Tests/AuthenticationEmailTemplatesTests.cs`:

```csharp
using TempoDesk.Application.Authentication;

namespace TempoDesk.Application.Tests;

public sealed class AuthenticationEmailTemplatesTests
{
    [Fact]
    public void VerificationContainsSubjectCopyAndEncodedFrontendLink()
    {
        var content = AuthenticationEmailTemplates.Verification(
            new Uri("https://app.example/tempo"), "token/+?");
        const string link =
            "https://app.example/tempo/verify-email?token=token%2F%2B%3F";

        Assert.Equal("Verify your TempoDesk email", content.Subject);
        Assert.Contains("Verify your email", content.TextBody,
            StringComparison.Ordinal);
        Assert.Contains(link, content.TextBody, StringComparison.Ordinal);
        Assert.Contains("Verify email", content.HtmlBody,
            StringComparison.Ordinal);
        Assert.Contains($"href=\"{link}\"", content.HtmlBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordResetContainsSubjectCopyAndEncodedFrontendLink()
    {
        var content = AuthenticationEmailTemplates.PasswordReset(
            new Uri("https://app.example/tempo/"), "reset/+?");
        const string link =
            "https://app.example/tempo/reset-password?token=reset%2F%2B%3F";

        Assert.Equal("Reset your TempoDesk password", content.Subject);
        Assert.Contains("Reset your password", content.TextBody,
            StringComparison.Ordinal);
        Assert.Contains(link, content.TextBody, StringComparison.Ordinal);
        Assert.Contains("Reset password", content.HtmlBody,
            StringComparison.Ordinal);
        Assert.Contains($"href=\"{link}\"", content.HtmlBody,
            StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Verify RED**

Run:

```bash
dotnet test server/tests/TempoDesk.Application.Tests --filter FullyQualifiedName~AuthenticationEmailTemplatesTests -m:1
```

Expected: compilation fails because `AuthenticationEmailTemplates` does not exist.

- [ ] **Step 4: Add the minimal templates**

Create `server/src/TempoDesk.Application/Authentication/AuthenticationEmailTemplates.cs`:

```csharp
using System.Net;

namespace TempoDesk.Application.Authentication;

public sealed record EmailContent(
    string Subject,
    string TextBody,
    string HtmlBody);

public static class AuthenticationEmailTemplates
{
    public static EmailContent Verification(
        Uri frontendBaseUrl,
        string token) => Create(
            "Verify your TempoDesk email",
            "Verify your email",
            "Verify email",
            "If you did not create a TempoDesk account, ignore this email.",
            frontendBaseUrl,
            "verify-email",
            token);

    public static EmailContent PasswordReset(
        Uri frontendBaseUrl,
        string token) => Create(
            "Reset your TempoDesk password",
            "Reset your password",
            "Reset password",
            "If you did not request a password reset, ignore this email.",
            frontendBaseUrl,
            "reset-password",
            token);

    private static EmailContent Create(
        string subject,
        string heading,
        string action,
        string fallback,
        Uri frontendBaseUrl,
        string route,
        string token)
    {
        ArgumentNullException.ThrowIfNull(frontendBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var link = $"{frontendBaseUrl.AbsoluteUri.TrimEnd('/')}/{route}" +
            $"?token={Uri.EscapeDataString(token)}";
        var encodedLink = WebUtility.HtmlEncode(link);

        return new EmailContent(
            subject,
            $"{heading}\n\n{link}\n\n{fallback}",
            $"<p>{heading}</p><p><a href=\"{encodedLink}\">{action}</a></p>" +
            $"<p>{fallback}</p>");
    }
}
```

- [ ] **Step 5: Verify GREEN**

```bash
dotnet test server/tests/TempoDesk.Application.Tests --filter FullyQualifiedName~AuthenticationEmailTemplatesTests -m:1
```

Expected: 2 tests pass.

- [ ] **Step 6: Commit Task 1**

```bash
git add server/src/TempoDesk.Application/Authentication/AuthenticationEmailTemplates.cs server/tests/TempoDesk.Application.Tests/AuthenticationEmailTemplatesTests.cs
git commit -m "feat(email): add authentication email templates"
```

---

### Task 2: Frontend URL configuration

**Files:**
- Modify: `server/src/TempoDesk.Infrastructure/Configuration/FoundationOptions.cs`
- Modify: `server/tests/TempoDesk.Application.Tests/ConfigurationTests.cs`

**Interfaces:**
- Produces: `EmailOptions.FrontendBaseUrl` as `string`.
- Consumes: existing `FoundationConfiguration.HttpUrl` validation.

- [ ] **Step 1: Write failing configuration tests**

Append to `ConfigurationTests`:

```csharp
[Fact]
public void EnabledEmailAcceptsValidFrontendBaseUrl()
{
    using var provider = Create(new()
    {
        ["Database:Password"] = "private-password",
        ["Email:Enabled"] = "true",
        ["Email:Host"] = "localhost",
        ["Email:From"] = "no-reply@tempodesk.local",
        ["Email:FrontendBaseUrl"] = "http://localhost:5173"
    });

    Assert.Equal(
        "http://localhost:5173",
        provider.GetRequiredService<IOptions<EmailOptions>>()
            .Value.FrontendBaseUrl);
}

[Theory]
[InlineData("")]
[InlineData("javascript:alert(1)")]
[InlineData("//example.com")]
public void EnabledEmailRejectsInvalidFrontendBaseUrl(string frontendBaseUrl)
{
    using var provider = Create(new()
    {
        ["Database:Password"] = "private-password",
        ["Email:Enabled"] = "true",
        ["Email:Host"] = "localhost",
        ["Email:From"] = "no-reply@tempodesk.local",
        ["Email:FrontendBaseUrl"] = frontendBaseUrl
    });

    Assert.Throws<OptionsValidationException>(() =>
        provider.GetRequiredService<IOptions<EmailOptions>>().Value);
}
```

- [ ] **Step 2: Verify RED**

```bash
dotnet test server/tests/TempoDesk.Application.Tests --filter "FullyQualifiedName~EnabledEmail" -m:1
```

Expected: compilation fails because `EmailOptions.FrontendBaseUrl` does not exist.

- [ ] **Step 3: Add the option and validation**

Add to `EmailOptions`:

```csharp
public string FrontendBaseUrl { get; set; } = "";
```

Replace the email binding predicate with:

```csharp
Bind<EmailOptions>("Email", o => !o.Enabled ||
    (!string.IsNullOrWhiteSpace(o.Host) &&
     o.Port is > 0 and <= 65535 &&
     System.Net.Mail.MailAddress.TryCreate(o.From, out _) &&
     HttpUrl(o.FrontendBaseUrl)));
```

- [ ] **Step 4: Verify GREEN**

```bash
dotnet test server/tests/TempoDesk.Application.Tests --filter FullyQualifiedName~ConfigurationTests -m:1
```

Expected: all configuration tests pass.

- [ ] **Step 5: Commit Task 2**

```bash
git add server/src/TempoDesk.Infrastructure/Configuration/FoundationOptions.cs server/tests/TempoDesk.Application.Tests/ConfigurationTests.cs
git commit -m "feat(email): configure frontend email links"
```

---

### Task 3: Local SMTP verification sender

**Files:**
- Create: `server/src/TempoDesk.Infrastructure/Email/SmtpEmailVerificationSender.cs`
- Delete: `server/src/TempoDesk.Infrastructure/Identity/DeferredEmailVerificationSender.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs`
- Create: `server/tests/TempoDesk.Application.Tests/EmailDeliveryTests.cs`

**Interfaces:**
- Consumes: unchanged `IEmailVerificationSender`, `VerificationEmail`, `IOptions<EmailOptions>`, and `AuthenticationEmailTemplates.Verification`.
- Produces: scoped `SmtpEmailVerificationSender` registration for `IEmailVerificationSender`.

- [ ] **Step 1: Write failing DI and disabled-delivery tests**

Create `server/tests/TempoDesk.Application.Tests/EmailDeliveryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Application.Tests;

public sealed class EmailDeliveryTests
{
    [Fact]
    public void IdentityRegistersTheLocalSmtpVerificationSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTempoDeskIdentity();
        var descriptor = Assert.Single(services.Where(service =>
            service.ServiceType == typeof(IEmailVerificationSender)));

        Assert.Equal(
            "SmtpEmailVerificationSender",
            descriptor.ImplementationType?.Name);
    }

    [Fact]
    public async Task DisabledEmailReportsDeliveryUnavailableWithoutConnecting()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTempoDeskIdentity();
        services.AddSingleton<IOptions<EmailOptions>>(
            Options.Create(new EmailOptions()));
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailVerificationSender>();

        var delivered = await sender.SendAsync(
            new VerificationEmail(
                "person@example.com",
                "raw-token",
                DateTimeOffset.UtcNow.AddMinutes(30)),
            CancellationToken.None);

        Assert.False(delivered);
    }
}
```

- [ ] **Step 2: Verify RED**

```bash
dotnet test server/tests/TempoDesk.Application.Tests --filter FullyQualifiedName~EmailDeliveryTests -m:1
```

Expected: the DI test fails because the registered type is `DeferredEmailVerificationSender`.

- [ ] **Step 3: Add the SMTP sender**

Create `server/src/TempoDesk.Infrastructure/Email/SmtpEmailVerificationSender.cs`:

```csharp
using System.Net.Mail;
using System.Net.Mime;
using Microsoft.Extensions.Options;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;

namespace TempoDesk.Infrastructure.Email;

internal sealed class SmtpEmailVerificationSender(
    IOptions<EmailOptions> emailOptions) : IEmailVerificationSender
{
    private readonly EmailOptions options = emailOptions.Value;

    public async Task<bool> SendAsync(
        VerificationEmail message,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
            return false;

        var content = AuthenticationEmailTemplates.Verification(
            new Uri(options.FrontendBaseUrl), message.Token);
        using var email = new MailMessage
        {
            From = new MailAddress(options.From),
            Subject = content.Subject,
            Body = content.TextBody
        };
        email.To.Add(message.Email);
        email.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            content.TextBody, null, MediaTypeNames.Text.Plain));
        email.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            content.HtmlBody, null, MediaTypeNames.Text.Html));

        using var smtp = new SmtpClient(options.Host, options.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        await smtp.SendMailAsync(email, cancellationToken);
        return true;
    }
}
```

- [ ] **Step 4: Replace the deferred registration**

Delete `DeferredEmailVerificationSender.cs`, add `using TempoDesk.Infrastructure.Email;` to `IdentityConfiguration.cs`, then register:

```csharp
services.TryAddScoped<IEmailVerificationSender,
    SmtpEmailVerificationSender>();
```

- [ ] **Step 5: Verify GREEN and registration compatibility**

```bash
dotnet test server/tests/TempoDesk.Application.Tests --filter FullyQualifiedName~EmailDeliveryTests -m:1
dotnet test server/tests/TempoDesk.IntegrationTests --filter FullyQualifiedName~RegistrationIntegrationTests -m:1
```

Expected: email delivery tests and all registration integration tests pass.

- [ ] **Step 6: Commit Task 3**

```bash
git add server/src/TempoDesk.Infrastructure/Email/SmtpEmailVerificationSender.cs server/src/TempoDesk.Infrastructure/Identity/DeferredEmailVerificationSender.cs server/src/TempoDesk.Infrastructure/Identity/IdentityConfiguration.cs server/tests/TempoDesk.Application.Tests/EmailDeliveryTests.cs
git commit -m "feat(email): send verification mail over local SMTP"
```

---

### Task 4: Mailpit local runtime wiring

**Files:**
- Modify: `server/src/TempoDesk.Api/appsettings.Development.json`
- Modify: `docker-compose.yml`
- Modify: `.env.example`

**Interfaces:**
- Produces: Mailpit SMTP at `mailpit:1025` inside Compose.
- Produces: Mailpit inbox at `http://localhost:${MAILPIT_PORT:-8025}`.
- Produces: API `Email__*` values consumed by `EmailOptions`.

- [ ] **Step 1: Verify the local inbox is currently absent**

```bash
docker compose config --services | rg -x mailpit
```

Expected: exit 1 because no `mailpit` service exists.

- [ ] **Step 2: Add development settings**

Add to `appsettings.Development.json`:

```json
"Email": {
  "Enabled": true,
  "Host": "localhost",
  "Port": 1025,
  "From": "no-reply@tempodesk.local",
  "FrontendBaseUrl": "http://localhost:5173"
}
```

- [ ] **Step 3: Add Mailpit and API overrides**

Add under Compose `services`:

```yaml
  mailpit:
    image: axllent/mailpit:v1.32.0
    ports:
      - "127.0.0.1:${MAILPIT_PORT:-8025}:8025"
```

Add to API environment:

```yaml
      Email__Enabled: "true"
      Email__Host: mailpit
      Email__Port: "1025"
      Email__From: no-reply@tempodesk.local
      Email__FrontendBaseUrl: http://localhost:${WEB_PORT:-5173}
```

Add to API `depends_on`:

```yaml
      mailpit:
        condition: service_started
```

- [ ] **Step 4: Add the inbox port to `.env.example`**

```dotenv
MAILPIT_PORT=8025
```

- [ ] **Step 5: Verify Compose wiring**

```bash
docker compose --env-file .env.example config
```

Expected: exit 0; output contains `axllent/mailpit:v1.32.0`, API host `mailpit`, SMTP port `1025`, and localhost inbox port `8025`.

- [ ] **Step 6: Commit Task 4**

```bash
git add .env.example docker-compose.yml server/src/TempoDesk.Api/appsettings.Development.json
git commit -m "feat(email): add Mailpit local inbox"
```

---

### Task 5: Full verification

**Files:**
- No production changes unless verification identifies a scoped defect.

**Interfaces:**
- Verifies all SCRUM-21 outputs and existing server callers together.

- [ ] **Step 1: Run the complete server test suite**

```bash
dotnet test server/TempoDesk.sln -m:1
```

Expected: every test project passes with zero failures.

- [ ] **Step 2: Build Release**

```bash
dotnet build server/TempoDesk.sln --configuration Release --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 3: Revalidate Compose**

```bash
docker compose --env-file .env.example config --quiet
```

Expected: exit 0.

- [ ] **Step 4: Confirm scoped changes and ignored docs**

```bash
git status --short
git diff HEAD -- . ':!docs'
git ls-files docs
```

Expected: only SCRUM-21 files are committed or intentionally modified; `git ls-files docs` prints nothing.

- [ ] **Step 5: Check acceptance criteria from fresh evidence**

- Mailpit captures SMTP email and exposes a localhost inbox.
- Both templates have verified subject, text, HTML, and configured frontend URLs.
- New code does not log passwords or raw tokens.
- Registration's public interface and enumeration-safe failure behavior remain unchanged.
