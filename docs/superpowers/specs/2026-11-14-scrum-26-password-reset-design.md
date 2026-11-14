# SCRUM-26 Forgot and reset password design

## Goal

Add a safe email-based forgot/reset password flow that does not reveal whether
an account exists. Reset tokens are short-lived, single-use, stored only as
hashes, and a successful password change revokes every refresh-token session
for the account.

## Scope

This ticket adds:

- a neutral forgot-password API;
- password-reset token issuance and persistence;
- reset-link email delivery through the existing SMTP configuration;
- a reset-password API that enforces the existing Identity password policy;
- atomic single-use token consumption, password update, and session revocation;
- basic reset-request/reset-result audit events;
- API and integration tests for enumeration resistance and token lifecycle.

It does not add frontend screens, administrator resets, access-token
denylisting, rate limiting, audit enrichment, or a database migration. Rate
limits and enriched audit context belong to SCRUM-27.

## Existing foundation

The database already has `public.password_reset_tokens` with token ID, user ID,
SHA-256 hash, expiry, usage, and creation timestamps. The application already
has a verified password-reset email template, SMTP configuration, ASP.NET Core
Identity password policy, rotating refresh tokens, and account-wide session
revocation from SCRUM-25.

## API contracts

### Forgot password

`POST /api/auth/forgot-password` accepts:

```json
{
  "email": "person@example.com"
}
```

Every valid request returns HTTP 202 with the same response, whether the email
is unknown, known, disabled for delivery, or races with another request:

```json
{
  "message": "If the account exists, a password reset email will be sent."
}
```

Normal model validation still returns HTTP 400 for a malformed email. The
response never contains a token, user ID, or account-existence signal.

### Reset password

`POST /api/auth/reset-password` accepts:

```json
{
  "token": "<opaque reset token>",
  "newPassword": "<new password>"
}
```

A successful reset returns HTTP 204. Missing, malformed, unknown, expired, or
already-used tokens return the same HTTP 400 validation response:

```json
{
  "errors": {
    "token": ["The reset token is invalid or expired."]
  }
}
```

When the token is valid but the password fails the existing Identity policy,
the API returns HTTP 400 validation errors without consuming the token. The
caller may retry the same token with a compliant password until it expires.

## Token lifecycle

For an existing account, forgot-password processing:

1. starts a database transaction and locks the user row;
2. marks every unused reset token for the user as used;
3. generates 32 random bytes encoded as unpadded base64url;
4. stores only the lowercase SHA-256 hash with a 30-minute expiry;
5. writes `password.reset.requested` audit success;
6. commits before attempting email delivery;
7. sends a reset link using `AuthenticationEmailTemplates.PasswordReset`.

Unknown accounts produce a `password.reset.requested` audit failure with no
user ID and return the same neutral HTTP response. Email delivery failure does
not change the HTTP response and never causes the raw token to be logged.

The reset endpoint rejects raw tokens that are blank or longer than 512
characters before hashing. It locates a candidate by hash, then uses row
locking and a conditional `used_at IS NULL AND expires_at > now` update so at
most one concurrent reset can consume the token.

## Password update and session revocation

After claiming a valid token, the service loads the account and asks Identity
to apply the new password. Identity remains the single source of password
policy and password-hash behavior.

Token consumption, password update, refresh-token revocation, and the
`password.reset` success audit commit in one database transaction. The existing
session repository must join an ambient transaction instead of opening a
nested transaction when reset-password calls account-wide revocation.

If Identity rejects the new password, the transaction rolls back so the token
remains usable. If any persistence or session-revocation step fails, the whole
reset rolls back.

Revocation prevents every old session from refreshing. Already-issued access
tokens remain valid until their configured short expiry; an access-token
denylist is outside this ticket.

## Components

### Application contracts

Add a password-reset service with:

- `ForgotPasswordAsync(email)` returning no account-existence information;
- `ResetPasswordAsync(token, newPassword)` returning success, invalid-token,
  or password-policy errors.

Add a dedicated password-reset email sender contract. It receives the target
email and raw token only in memory and returns whether delivery was attempted
successfully.

### Infrastructure

Map a `PasswordResetToken` entity to the existing table and indexes. Implement
issuance, hash lookup, row locking, conditional consumption, Identity password
update, auditing, and SMTP delivery in Infrastructure.

The reset service reuses `IAuthenticationSessionService.RevokeAllAsync`; its
repository operation is adjusted to participate in an existing EF transaction.

### API

Add request DTOs with the same email and password validation conventions as
registration/login. The controller translates only the application result and
does not query Identity or the database.

## Auditing and sensitive data

Write:

- `password.reset.requested` for known and unknown reset requests;
- `password.reset` for successful and rejected reset attempts.

Audit rows contain success, time, and user ID when known. They contain no email,
password, raw token, token hash, cookie, Authorization header, or JWT. SCRUM-27
will automatically add safe request context such as trace ID and IP address.

## Testing

Use test-first changes to cover:

- EF mapping to the existing password-reset table;
- identical forgot-password API responses for existing and missing accounts;
- only token hashes being persisted;
- previous unused tokens being invalidated when a new one is issued;
- reset email containing the encoded reset link and no token in logs/audits;
- valid token success and successful login with the new password;
- old password rejection after reset;
- invalid, expired, and reused tokens sharing one safe response;
- password-policy rejection preserving token usability;
- concurrent use allowing at most one successful reset;
- successful reset revoking every existing refresh-token family;
- persistence/session failure rolling back token and password changes;
- existing registration, verification, login, refresh, and session tests
  continuing to pass.

## Out of scope

- Frontend forgot/reset password screens.
- Rate limiting; implemented by SCRUM-27.
- Password history beyond the current Identity policy.
- Immediate access-token revocation.
- Scheduled token cleanup.
- Database schema changes.
