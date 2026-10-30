# SCRUM-20 User Registration and Email Verification Design

## Goal

Allow a user to register with an email address and password, prove ownership of that email with a single-use expiring token, and request a replacement token without exposing whether an account exists.

## Scope

This ticket implements registration, verification-token persistence, verification, resend, audit records, and API coverage. It defines an email-delivery abstraction and uses a capturing fake in tests. Local email capture, templates, and verification-link construction remain outside this ticket.

Roles, login, access tokens, refresh tokens, password reset, rate limiting, and frontend screens are out of scope.

## Layer Boundaries

`TempoDesk.Application` owns the registration use-case contracts and result types. It does not reference ASP.NET Core Identity, EF Core, Npgsql, or HTTP types.

`TempoDesk.Infrastructure` implements the use cases with `UserManager<ApplicationUser>` and `TempoDeskIdentityDbContext`. It owns random-token generation, SHA-256 hashing, persistence mappings, transactions, and the default deferred email sender.

`TempoDesk.Api` exposes three controller actions and maps typed application results to HTTP responses. Passwords and raw tokens are never logged or included in error responses.

## API

### `POST /api/auth/register`

Request:

```json
{
  "email": "person@example.com",
  "password": "Correct-Horse-7-Battery!"
}
```

The API normalizes the email through Identity. A new address creates an unconfirmed user and issues a verification token. An existing unconfirmed address behaves like a resend. An existing confirmed address performs no write. All three cases return `202 Accepted` with the same response shape so the endpoint does not disclose whether the address exists.

Identity password-policy failures return `400 Bad Request` with stable validation codes and no password content.

### `POST /api/auth/verify-email`

Request:

```json
{
  "token": "opaque-url-safe-token"
}
```

A valid unused token confirms the user and returns `204 No Content`. Invalid, expired, or previously consumed tokens all return the same `400 Bad Request` response.

### `POST /api/auth/resend-verification`

Request:

```json
{
  "email": "person@example.com"
}
```

If the address belongs to an unconfirmed account, all previous unused tokens are invalidated and a replacement is issued. Missing and already-confirmed accounts perform no write. Every case returns the same `202 Accepted` response.

## Token Lifecycle

Each token contains 32 cryptographically random bytes encoded with Base64 URL encoding. Only its lowercase SHA-256 hexadecimal hash is stored in `email_verification_tokens`; the raw value exists only long enough to pass to `IEmailVerificationSender`.

Tokens expire 30 minutes after issuance. Resend invalidates previous unused tokens before inserting the replacement. Verification claims a token with a conditional database update inside the same transaction that confirms the user. The conditional update requires matching hash, `used_at IS NULL`, and `expires_at > now`, so concurrent requests cannot both succeed.

The default sender reports delivery unavailable after persistence; this keeps startup valid while email delivery is deferred. Register and resend still return the same `202 Accepted` response so delivery failures cannot reveal whether an account exists. The failure is recorded as a security audit event, and the unconfirmed account remains recoverable through resend after an email implementation is installed. Integration tests replace the sender with a capturing fake.

## Persistence

Infrastructure adds manual EF mappings for the existing `email_verification_tokens` and `security_audit_logs` tables. No generated files or SQL migrations are changed.

The database remains the source of truth. Registration relies on the existing unique normalized-email index for race-safe duplicate prevention. Identity duplicate-email errors are converted to the same accepted result used for an already-existing account.

Audit events include the event type, success flag, optional user ID, and timestamp. They never contain a password, raw token, token hash, or full email address.

## Error Handling

- Malformed email, empty token, and password-policy failures: `400`.
- Duplicate, missing, or already-confirmed email during register/resend: generic `202`.
- Invalid, expired, or used verification token: generic `400`.
- Deferred email sender: generic `202`; delivery failure is recorded without email or token data.
- Unexpected database failures: existing request-boundary handling returns sanitized `500`.

## Testing

Application tests cover result-to-error behavior without referencing Identity.

PostgreSQL integration tests use the real runtime database role and the existing SQL migrations. They cover:

- normalized duplicate email cannot create a second user;
- new accounts are unconfirmed;
- raw tokens are not stored;
- a valid token confirms exactly once;
- expired and used tokens are rejected;
- resend invalidates the previous token;
- password and token values do not appear in audit metadata;
- fake email delivery receives the raw token only in test memory.

API tests cover the three endpoints, generic enumeration-safe responses, consistent problem details, and the deferred-sender `202` behavior.

## Operational Follow-up

The next email-delivery ticket replaces the deferred sender with a local capture implementation and builds verification links from configured frontend base URL. Production-grade delivery and durable Data Protection keys remain deployment concerns.
