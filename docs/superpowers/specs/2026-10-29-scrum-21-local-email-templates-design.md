# SCRUM-21 Local Email Service and Authentication Templates Design

## Goal

Deliver registration verification emails to a local inbox and provide reusable verification and password-reset email templates without coupling Application code to SMTP or a deployment environment.

## Scope

This ticket adds HTML and plain-text authentication email templates, constructs links from a configured frontend base URL, replaces the deferred verification sender with a local SMTP implementation, and adds a Mailpit inbox to the local Docker stack.

Password-reset token generation, password-reset API endpoints, frontend authentication screens, production email providers, retries, queues, and delivery tracking are outside this ticket.

## Architecture

`TempoDesk.Application` keeps the existing `IEmailVerificationSender` boundary used by registration. It also owns framework-free authentication template rendering because subjects, body copy, and frontend routes are application behavior.

Templates return a small email-content value containing subject, plain text, and HTML. They accept a validated frontend base URI and opaque token. Verification links use `/verify-email?token=...`; password-reset links use `/reset-password?token=...`. Tokens are URL-escaped and never logged.

`TempoDesk.Infrastructure` replaces `DeferredEmailVerificationSender` with an SMTP implementation. The sender renders the verification template, creates a multipart plain-text/HTML message, and sends it to the configured SMTP host and port using the platform email API. It returns `true` only after SMTP accepts the message; the existing registration service retains its current delivery-failure handling and audit behavior.

`EmailOptions` gains `FrontendBaseUrl`. When email is enabled, configuration validation requires a valid HTTP(S) frontend base URL in addition to the existing host, port, and sender address checks.

## Local Runtime

Docker Compose adds Mailpit with SMTP available to the API inside the network and its web inbox bound to localhost. The API container receives explicit email settings pointing to Mailpit and the configured local web origin. Development outside Docker uses localhost defaults from development settings.

No username, password, TLS, provider-specific option, retry policy, or background queue is added because the local capture server does not require them.

## Error Handling and Security

- SMTP failures propagate to the existing `AccountRegistrationService.DeliverAsync` boundary, which records a failed delivery without changing the enumeration-safe API response.
- Cancellation remains observable and is not converted into a delivery failure.
- Passwords, reset tokens, and verification tokens are not logged or placed in exception messages by new code.
- HTML content encodes user-controlled values; URLs encode opaque tokens.
- Email messages are disposed after sending.

## Testing

Application unit tests verify each template's exact subject, expected human-readable content, and frontend URL in both text and HTML forms. Tests also cover URL encoding and confirm that a non-root frontend base path is preserved.

Configuration tests verify that enabled email requires a valid HTTP(S) frontend base URL.

Infrastructure registration integration tests continue replacing the sender with their existing in-memory capture. No Docker-dependent SMTP test is added; Mailpit is checked through the existing local stack workflow.

## Files and Callers

The change reaches:

- Application authentication contracts and new template tests;
- Infrastructure email sender and dependency injection registration;
- `EmailOptions` validation and its configuration tests;
- API development settings and Docker Compose environment;
- `.env.example` for the local inbox port;
- existing registration callers through the unchanged `IEmailVerificationSender` interface.

No database schema, registration API contract, frontend source, or production provider integration changes.
