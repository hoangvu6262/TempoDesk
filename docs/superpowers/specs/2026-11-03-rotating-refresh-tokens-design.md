# Rotating refresh tokens and reuse detection design

## Goal

Add rotating refresh tokens to the existing email/password login flow. A valid refresh token can be used once to obtain a new access token and replacement refresh token. Reusing a rotated token atomically revokes its entire token family.

Google login, logout, manual session revocation, device management UI, password reset, and rate limiting are outside SCRUM-24.

## Existing foundation

SCRUM-22 already issues short-lived JWT access tokens. The existing `public.refresh_tokens` table already contains the required columns and indexes:

- token ID, user ID, and family ID;
- SHA-256 token hash;
- expiry, used, and revoked timestamps;
- replacement-token foreign key;
- optional device metadata.

No database migration is required. Add an EF Core entity and mapping for the existing table.

## Token transport and API contracts

The raw refresh token is a cryptographically random, URL-safe value. It is returned only in a cookie named `tempodesk.refresh_token` with:

- `HttpOnly = true`;
- `SameSite = Strict`;
- `Path = /api/auth`;
- expiry matching the persisted refresh-token expiry;
- `Secure = true` whenever the request uses HTTPS.

The raw refresh token never appears in JSON, logs, audit metadata, or the database. The database stores only its lowercase SHA-256 hash.

`POST /api/auth/login` keeps its existing JSON response:

```json
{
  "accessToken": "<jwt>",
  "tokenType": "Bearer",
  "expiresIn": 900
}
```

It additionally sets the initial refresh-token cookie after successful credential validation.

Add `POST /api/auth/refresh`. It has no request body and reads the refresh token from the cookie. Success returns the same access-token JSON contract and replaces the cookie. Failure returns the existing generic HTTP 401 ProblemDetails contract and expires the cookie.

Because the cookie uses `SameSite=Strict` and authentication CORS is restricted, no separate CSRF token is added in this ticket.

## Configuration

Extend `AuthenticationOptions` with `RefreshTokenDays`, defaulting to 30. When authentication is enabled, accept values from 1 through 90 days and reject values outside that range without exposing the signing key or token values.

Access-token lifetime continues to use `AccessTokenMinutes`.

## Components

### Shared access-token issuer

Move JWT creation out of `AccountLoginService` into one Infrastructure service used by both Login and Refresh. It creates the same HMAC-SHA256 token, issuer, audience, expiry, and minimal claims already verified by SCRUM-22. This is the only behavior-preserving extraction in the ticket.

### Refresh-token service

Add an Application contract for:

- issuing the first refresh token for a successfully authenticated user;
- rotating a presented refresh token;
- returning either a complete token pair or one generic rejected result.

The Infrastructure implementation coordinates token generation, access-token issuance, auditing, persistence, and transactions.

### Refresh-token repository

Add a real repository boundary for the refresh-token table because this feature requires row locking, family updates, replacement links, and atomic transactions. It is not a wrapper around `UserManager` or an Identity store.

The repository implementation uses the shared scoped `TempoDeskIdentityDbContext` and PostgreSQL `SELECT ... FOR UPDATE` when loading a presented token hash.

## Initial issuance

After Identity accepts email/password credentials:

1. Generate 32 cryptographically random bytes and encode them as unpadded base64url.
2. Hash the raw token with SHA-256.
3. Create a new token row whose `family_id` equals a new family GUID.
4. Set expiry from `RefreshTokenDays`.
5. Issue the access token through the shared access-token issuer.
6. Save the refresh-token row and successful login audit.
7. Return the raw refresh token only to the API controller so it can set the cookie.

Credential failures retain the existing generic response and do not issue a cookie.

## Rotation transaction

For a presented refresh token:

1. Validate that it is nonblank and within a defensive maximum length, then hash it.
2. Begin a database transaction.
3. Lock the matching refresh-token row by hash using `FOR UPDATE`.
4. Reject missing, expired, or revoked tokens with the same external contract.
5. If `used_at` or `replaced_by_token_id` is already set, mark every non-revoked token in the family revoked inside the same transaction, write a reuse audit event, commit, and reject.
6. Otherwise, generate a replacement token in the same family.
7. Set the current row's `used_at` and `replaced_by_token_id`.
8. Insert the replacement row, write a successful refresh audit event, issue the access token, and commit.
9. Return the replacement raw token and access token.

The service never accepts a user ID, family ID, or expiry from the client.

## Concurrent refresh behavior

PostgreSQL row locking serializes concurrent requests that present the same token. At most one request rotates it successfully. A later waiter observes the committed `used_at`, treats the request as reuse, and revokes the whole family, including the newly created replacement.

This strict behavior intentionally prefers theft detection over preserving a session after duplicate client requests. Clients must serialize refresh requests.

## Auditing and safe failures

Use security audit events:

- `token.refresh` for successful and ordinary failed refresh attempts;
- `token.refresh.reuse` for reuse detection and family revocation.

Audit rows contain only success/failure, time, and user ID when known. They never contain raw tokens, hashes, cookies, or JWTs.

Invalid, missing, expired, revoked, and reused refresh tokens all return the same HTTP 401 response. Internal result variants may distinguish reuse for auditing but must not reach the API contract.

## Tests

Use test-first changes to cover:

- configuration accepts valid and rejects invalid refresh lifetimes;
- login stores only a hash and sets the expected cookie flags;
- a valid refresh token works once and returns a replacement cookie plus new access token;
- missing, malformed, expired, and revoked tokens are rejected safely;
- reuse of a rotated token revokes every token in its family;
- concurrent use of one token permits at most one rotation and leaves the family revoked after reuse detection;
- audit storage contains no raw refresh token, token hash, or JWT;
- all existing registration, email, login, and JWT tests continue to pass.

## Out of scope

- Google or other external login providers.
- Logout, explicit session revocation, session listing, or device naming.
- Populating `device_name`, `user_agent`, or `ip_address`.
- Refresh-token cleanup jobs or partitioning.
- Returning refresh tokens in JSON.
