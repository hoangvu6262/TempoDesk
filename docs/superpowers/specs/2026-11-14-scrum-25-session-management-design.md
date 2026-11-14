# SCRUM-25 Session management design

## Goal

Let an authenticated user inspect and control their own login sessions. Add
logout, session listing, single-session revocation, and account-wide session
revocation on top of the rotating refresh-token implementation from SCRUM-24.

Each refresh-token family represents one login session. This uses the existing
schema and avoids a second session table.

## Scope

This ticket adds:

- logout by revoking the refresh-token family identified by the current cookie;
- an authenticated list of the caller's sessions;
- authenticated revocation of one session owned by the caller;
- authenticated revocation of all sessions owned by the caller;
- device metadata captured from the login request;
- audit events and API/integration coverage for authorization, ownership, and
  idempotency.

It does not add a session-management UI, user-agent parsing, access-token
denylisting, configurable device names, cleanup jobs, or a database migration.

## Existing foundation

SCRUM-24 already provides login, refresh-token cookies, one-time rotation,
reuse detection, row locking, family revocation after reuse, and safe audit
events. The `public.refresh_tokens` table already contains `family_id`,
`device_name`, `user_agent`, `ip_address`, creation/usage/expiry/revocation
timestamps, and replacement links.

The EF model currently maps the token lifecycle fields but not the three device
metadata fields. The refresh-token application contracts currently expose only
initial issuance and rotation.

## API contracts

### Logout

`POST /api/auth/logout` reads `tempodesk.refresh_token`, revokes the matching
token family when it can be identified, deletes the cookie, and returns HTTP
204. A missing, unknown, expired, or already-revoked token also returns 204 and
deletes the cookie, making logout idempotent without disclosing token state.

Logout does not require a bearer token. Possession of the HttpOnly refresh
cookie identifies the session to revoke, and the cookie remains protected by
the existing `SameSite=Strict` policy and restricted authentication CORS.

### List sessions

`GET /api/auth/sessions` requires a valid bearer access token. It derives the
user ID exclusively from the authenticated principal and returns only that
user's refresh-token families. The response is ordered by most recent activity
and contains:

```json
[
  {
    "id": "<family UUID>",
    "userAgent": "<raw User-Agent or null>",
    "ipAddress": "<login IP or null>",
    "createdAt": "<UTC timestamp>",
    "lastUsedAt": "<UTC timestamp>",
    "expiresAt": "<UTC timestamp>",
    "status": "active | revoked | expired"
  }
]
```

For each family, `createdAt` is its earliest token creation time,
`lastUsedAt` is its latest `used_at` value or `createdAt` when it has never
rotated, and `expiresAt` is the latest token expiry. A family is `revoked` when
all of its tokens are revoked, `active` when it has an unrevoked and unexpired
token, and otherwise `expired`.

### Revoke one session

`DELETE /api/auth/sessions/{id}` requires a bearer access token. The path ID is
the family UUID. The operation updates only rows matching both the supplied
family ID and the authenticated user ID.

An owned active session is revoked and returns 204. An owned session that is
already revoked also returns 204. A missing family and a family owned by another
user both return 404 so the endpoint does not reveal another account's session
IDs.

### Revoke all sessions

`DELETE /api/auth/sessions` requires a bearer access token. It revokes every
unrevoked refresh token belonging to the authenticated user, including the
session that supplied the current access token, and returns 204. Repeated calls
also return 204.

The existing access token remains valid until its short expiry. Immediate
access-token denylisting is outside this ticket; all future refreshes for the
revoked sessions fail.

## Components and boundaries

### Session metadata

Extend the refresh-token persistence model and EF mapping with `DeviceName`,
`UserAgent`, and `IpAddress`. This requires no migration because the columns
already exist.

At login, the API passes request metadata through the application boundary.
Store at most the first 1,024 characters of the raw `User-Agent` in
`user_agent` and the remote IP address in `ip_address`. Do not parse the user
agent or add a dependency. Leave `device_name` null until a later ticket
defines naming behavior.

Refresh rotation copies the session metadata from the locked token to its
replacement so every row in a family retains the same session context.

### Session service

Add a small application service contract for logout, listing, revoking one
session, and revoking all sessions. Controllers are responsible only for HTTP
authentication, extracting the principal/cookie/request metadata, deleting the
cookie, and translating service results to status codes.

The Infrastructure implementation delegates database operations to the
existing refresh-token repository boundary and uses `TimeProvider` for status
calculation and audit timestamps.

### Repository operations

Extend the refresh-token repository with operations that:

- locate a raw token by its SHA-256 hash and revoke its family atomically;
- aggregate families for one user without returning token hashes;
- revoke one family with both `family_id` and `user_id` in the predicate;
- revoke every family for one user.

Mutations and their corresponding audit rows are saved in the same database
transaction. Repeated revocation does not alter the original revocation time.

## Authorization and ownership

Session list and explicit revocation endpoints use ASP.NET Core authorization.
The user ID comes from the JWT subject claim and is never accepted from the
request body, query string, or route.

Single-session revocation enforces ownership in the database predicate rather
than loading an arbitrary session and checking afterward. This prevents a
time-of-check/time-of-use gap and avoids exposing another user's session data.

Logout is scoped by the presented refresh token. It never accepts a user ID or
family ID from the client.

## Auditing and safe data handling

Write these security audit events:

- `auth.logout` for logout attempts, with the user ID when the token is known;
- `session.revoke` for owned single-session revocation attempts;
- `session.revoke_all` for account-wide revocation.

Audit rows contain success/failure, timestamp, and user ID when known. They do
not contain a raw refresh token, token hash, cookie, JWT, full user agent, or IP
address. Unknown and repeated logout attempts remain externally successful but
are recorded internally as unsuccessful audit events. Repeated revocation of
an owned session and repeated revoke-all calls are successful authorized
operations and are audited as successful even when no token row changes.

## Error handling and idempotency

- Missing or malformed bearer authentication returns the framework's 401.
- Missing/foreign single-session IDs return the same 404 response.
- Logout and revoke-all always return 204 after safe processing.
- Repeating logout, single-session revocation of an owned session, or revoke-all
  does not create a new state change or replace the original revocation time.
- Database failures flow through the existing request boundary and generic
  ProblemDetails contract; no token or account detail is exposed.

## Testing

Use test-driven changes to cover:

- metadata mapping and login capture without storing it in audit rows;
- metadata preservation during refresh rotation;
- logout revoking only the cookie's family and deleting the cookie;
- repeated and unknown-token logout returning 204;
- session listing returning only the authenticated user's families with correct
  ordering, timestamps, and status;
- list/revoke/revoke-all rejecting unauthenticated callers;
- a user being unable to list or revoke another user's session;
- one-session revocation leaving other sessions active;
- owned repeated revocation returning 204 without changing `revoked_at`;
- revoke-all invalidating every family for the user but no other user;
- expected audit events containing no token, hash, cookie, JWT, user agent, or
  IP address;
- all existing login, refresh, registration, Identity, and API tests continuing
  to pass.

## Out of scope

- Frontend session-management screens.
- Friendly browser/OS parsing or editable device names.
- Immediate JWT access-token revocation or a denylist.
- Global administrator session management.
- Session cleanup, retention, pagination, or rate limiting.
- Schema changes.
