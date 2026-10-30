# Email/password JWT login design

## Scope

Add `POST /api/auth/login` for verified Identity accounts. Successful login returns a short-lived bearer JWT. Invalid credentials, unverified accounts, and locked accounts return the same safe unauthorized contract. Every attempt is audited without storing credentials or tokens.

Refresh tokens, logout/revocation, roles, permissions, and client UI are outside this task.

## Configuration and authentication

Repurpose the currently unused `AuthenticationOptions` for the app-issued JWT:

- `Enabled`
- `Issuer`
- `Audience`
- `SigningKey`
- `AccessTokenMinutes`

When enabled, all values are validated at startup. The signing key must be at least 32 bytes and must never appear in errors or logs. The API registers JWT bearer authentication with issuer, audience, signature, and lifetime validation, then runs authentication and authorization middleware.

## Application and infrastructure

Add an application login contract that accepts email and password and returns either a token result or failure. Its infrastructure implementation uses the existing ASP.NET Identity `UserManager` and `SignInManager` so email normalization, password verification, failed-attempt counting, confirmed-email policy, and lockout policy remain centralized.

On success, issue an HMAC-SHA256 JWT with configured issuer, audience, expiry, and only the minimal claims `sub`, `jti`, and `iat`. Do not put email, password, security stamp, or other sensitive account data in the token.

Persist a `user.login` security audit row for every attempt. Store success/failure, time, and the user ID when a matching account exists; do not store the submitted email, password, or token.

## API contract

`POST /api/auth/login` accepts:

```json
{
  "email": "person@example.com",
  "password": "the account password"
}
```

Success returns HTTP 200:

```json
{
  "accessToken": "<jwt>",
  "tokenType": "Bearer",
  "expiresIn": 900
}
```

Unknown email, wrong password, unverified email, and lockout all return HTTP 401 with one generic `ProblemDetails` response. Request-shape validation remains HTTP 400 through the existing controller conventions.

## Tests

Use test-first changes to cover:

- API success response and the shared unauthorized response.
- Valid JWT signature, issuer, audience, expiry, and minimal claims.
- Wrong password increments failures and eventually locks the account.
- Unverified and locked accounts cannot receive tokens.
- Successful and failed attempts create safe audit rows.
- Configuration rejects missing, weak, or invalid JWT settings without leaking the signing key.

Run the focused tests during implementation, then the complete server test suite and build before completion.
