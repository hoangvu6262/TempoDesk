# SCRUM-29 Device session management UI design

## Goal

Let an authenticated TempoDesk user see the login sessions that belong to
their account, identify the session used by the current browser, revoke one
session, or revoke all sessions. The UI must never expose access tokens,
refresh tokens, or refresh-token hashes.

## Chosen approach

Add the session management experience to the existing protected `/app` page.
This is the smallest complete integration because `/app` is currently only a
placeholder and TempoDesk has no settings navigation yet.

The backend will explicitly return `isCurrent` for each session. The server
will determine this by hashing the HttpOnly refresh cookie and matching it to a
refresh-token family owned by the authenticated user. The client will not
guess that the newest or most recently active session is current.

Alternatives considered:

- A new `/app/settings/sessions` route would provide a more permanent
  information architecture, but it would also require navigation and settings
  shell work that the current application does not have.
- Inferring the current session from the first active row would avoid a backend
  change, but it is incorrect when the user has multiple active devices.

## Backend contract

`GET /api/auth/sessions` remains bearer-authenticated and continues to return
only sessions owned by the caller. Each item adds one field:

```json
{
  "id": "<family UUID>",
  "userAgent": "<raw User-Agent or null>",
  "ipAddress": "<login IP or null>",
  "createdAt": "<UTC timestamp>",
  "lastUsedAt": "<UTC timestamp>",
  "expiresAt": "<UTC timestamp>",
  "status": "active | revoked | expired",
  "isCurrent": true
}
```

The controller reads the existing `tempodesk.refresh_token` cookie and passes
it through the session service. The service validates its bounded token shape,
hashes it, and passes only the hash to persistence. The repository marks a
family current only when one of its rows matches that hash and the requested
user ID. Missing, malformed, expired, or already-revoked cookie values result
in every item returning `isCurrent: false`; they do not fail the list request.

The existing mutations stay unchanged:

- `DELETE /api/auth/sessions/{id}` revokes one owned family and returns 204;
- `DELETE /api/auth/sessions` revokes all families for the caller, including
  the current family, and returns 204.

## Frontend API and authentication state

Extend the existing authentication API adapter with:

- `listSessions()`;
- `revokeSession(id)`; and
- `revokeAllSessions()`.

The session DTO mirrors the server response. All calls reuse the configured
Axios client, which already supplies the in-memory bearer token and sends the
HttpOnly cookie with credentials.

After revoking a non-current session, the page reloads the server list. After
revoking the current session or all sessions, the provider clears its
in-memory access token immediately. The protected-route boundary then sends
the user to login. A stale HttpOnly cookie may remain in the browser, but it is
already revoked and cannot refresh; deleting it is not required for security
or correctness.

## Page and interaction design

Replace the placeholder contents of `/app` with a focused session-management
page while retaining the current application shell.

The page contains:

- a heading and concise explanation;
- a `Revoke all sessions` danger action;
- a list ordered as returned by the API;
- device/browser text derived conservatively from the raw user agent, with
  `Unknown device` as the fallback;
- IP address when present;
- created time, last activity, expiry, and status;
- a visible `Current session` badge when `isCurrent` is true; and
- a `Revoke` action for every active session.

Revoked and expired rows remain visible but have no revoke action. Raw user
agents may appear as secondary detail, but the UI never renders any token.
Dates use the browser locale and retain an accessible machine-readable
`datetime` value.

Both single-session and revoke-all actions use native confirmation dialogs.
This supplies keyboard operation, focus handling, and an explicit destructive
confirmation without introducing a modal component or dependency.

## UI states and feedback

- **Loading:** show a labelled spinner while the initial list is pending.
- **Empty:** explain that no sessions are available.
- **Success:** show a polite live-region message after a non-current session
  is revoked and the list refreshes.
- **Error:** show an alert with a retry action for list failures. Mutation
  failures keep the existing list and show an error alert.
- **Pending mutation:** disable the affected action and the revoke-all action
  to prevent duplicate destructive requests.

Success and error messages use `role="status"` or `role="alert"` so keyboard
and assistive-technology users receive the result without a focus trap.

## Components and boundaries

- `src/api/auth-api.ts` owns the session DTO and HTTP methods.
- `src/features/sessions/SessionManagementPage.tsx` owns fetching, mutation
  state, confirmation, and feedback.
- `src/features/sessions/session-display.ts` owns small pure formatting helpers
  for device labels and timestamps.
- `src/features/sessions/SessionManagementPage.scss` owns page-specific
  responsive layout and uses existing design tokens.
- `src/routes/AppRoutes.tsx` mounts the page at the protected `/app` route.
- Backend application and persistence contracts gain only the current-session
  input/output necessary for an authoritative `isCurrent` value.

No global state library, query library, user-agent parser, modal framework, or
new route hierarchy is added.

## Verification

The repository currently contains no frontend or server test projects, so the
implementation will not recreate the removed test infrastructure solely for
this ticket. Verification will cover:

- TypeScript production build;
- ESLint and Prettier checks;
- .NET solution build;
- static review of every session API caller after contract changes; and
- manual browser checks where the local API/database environment is available.

The key behaviors to verify are accurate current-session marking, loading and
empty states, single revoke list refresh, current-session logout, revoke-all
logout, failure feedback, disabled duplicate actions, keyboard confirmation,
and absence of token rendering or storage.

## Out of scope

- Editable device names or server-side user-agent parsing.
- Pagination, search, filtering, session history retention, or cleanup jobs.
- Access-token denylisting; an already-issued token is cleared locally and
  otherwise remains subject to its existing short expiry.
- Building a settings navigation system solely to host this page.
- Adding a component library or restoring the removed test suites.
