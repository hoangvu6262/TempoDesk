# SCRUM-28 frontend authentication design

## Goal

Build the React authentication experience for the existing TempoDesk APIs and
protect routes that require a signed-in user. The frontend must initialize the
session without exposing private content, preserve the requested URL across
login, silently refresh access tokens, and display API validation failures in a
useful form.

Google authentication is not implemented by the backend yet. This ticket adds
the disabled Google affordance and callback route needed by the frontend, but
does not simulate OAuth or expand the backend contract.

## Scope

This ticket adds:

- register, login, email verification, forgot-password, and reset-password
  pages;
- a disabled Google sign-in button with an explicit unavailable label;
- a Google callback page that does not consume credentials from the URL;
- an in-memory authentication provider;
- startup and scheduled silent refresh through the existing HttpOnly refresh
  cookie;
- a protected-route boundary with safe return navigation;
- consistent client validation and Problem Details presentation; and
- focused component and integration tests for the authentication behavior.

This ticket does not add Google OAuth backend endpoints, session management UI,
roles, permissions, persistent access-token storage, or unrelated application
features.

## Routes and screens

### Public routes

- `/` keeps the existing public development landing page.
- `/register` contains email, password, and password-confirmation fields. On
  success it displays the backend's neutral accepted message and prompts the
  user to check their email.
- `/verify-email?token=...` submits the token once on entry. It displays a
  pending state, then either a success state with a login link or an error state
  with a resend-verification email form.
- `/login` contains email and password fields. A successful login navigates to
  the validated `returnTo` query value or `/app` by default. The Google button
  is disabled and labelled `Chưa khả dụng` through visible supporting text.
- `/forgot-password` accepts an email and always presents the neutral response
  returned by the API.
- `/reset-password?token=...` accepts a new password and confirmation. A
  successful reset directs the user to login. A missing token is rejected
  locally without making a request.
- `/auth/google/callback` explains that Google sign-in is not configured. It
  never reads, stores, displays, or forwards OAuth-looking URL values.

All screens use the existing `AuthLayout`, form controls, buttons, alerts, and
design tokens. No new component library or form dependency is introduced.

### Protected route

`/app` is the first protected route and contains only a minimal signed-in page
needed to exercise the route boundary. While authentication is initializing,
the boundary renders a labelled loading state and no private child content. An
anonymous user is redirected to `/login?returnTo=%2Fapp`, including the original
query string and hash when present.

Only same-origin application paths beginning with exactly one `/` are accepted
as return destinations. Protocol-relative URLs, absolute URLs, backslashes, and
authentication pages are rejected and fall back to `/app`.

## API boundary

A small authentication API module uses the existing Axios client and the
configured API base URL. It exposes only the operations required by the pages:

- `POST /api/auth/register`;
- `POST /api/auth/verify-email`;
- `POST /api/auth/resend-verification`;
- `POST /api/auth/login`;
- `POST /api/auth/forgot-password`;
- `POST /api/auth/reset-password`; and
- `POST /api/auth/refresh`.

These paths are relative to the configured API base URL. The default `/api`
base is the reverse-proxy prefix and is stripped before forwarding, while the
`/api/auth` segment remains part of the ASP.NET controller route.

The API client reads the current access token through a callback when preparing
each request and adds a Bearer header only when a token exists. Passwords,
verification/reset tokens, access tokens, refresh cookies, and complete request
bodies are never logged.

## Authentication state and refresh

`AuthProvider` owns three observable session states:

- `initializing`: startup refresh has not completed;
- `authenticated`: an access token and its expiration are held in memory; and
- `anonymous`: no usable session exists.

The provider exposes the state plus `login` and `logoutLocal` actions. Login
stores the returned access token only in provider memory. The refresh token
remains in the server-managed HttpOnly cookie and is never accessible to React.
Neither access nor refresh tokens are written to local storage, session storage,
the URL, application logs, or rendered output.

On application startup, the provider makes one effective refresh request even
under React Strict Mode. A successful login or refresh schedules the next
refresh shortly before `expiresIn` elapses. The delay is clamped to a positive
value so unusually short lifetimes refresh safely without a tight loop.
Successful refresh replaces the in-memory access token and reschedules the
timer. A failed refresh cancels the timer, clears authentication state, and
leaves protected-route redirection to the route boundary.

The provider clears timers when it unmounts. Concurrent refresh triggers share
one in-flight request so refresh-token rotation cannot be raced by the client.

## Validation and error handling

Client validation prevents avoidable requests:

- email is required, syntactically valid, and at most 256 characters;
- passwords are required, between 12 and 1024 characters, and registration or
  reset confirmation must match;
- verification and reset tokens are required and at most 512 characters.

The server remains authoritative for the full Identity password policy. A
Problem Details `errors` entry is matched case-insensitively to its field and
the first message is rendered by `FormField`. Unmatched validation messages,
HTTP errors without field errors, network errors, timeouts, and rate-limit
responses appear in a page-level alert. Submit buttons enter a loading state and
prevent duplicate submission while a request is active.

Neutral register, resend-verification, and forgot-password responses are shown
without revealing whether an account exists. Authentication failures do not
distinguish an unknown email from an incorrect password.

## File boundaries

- `src/features/auth/api` owns request and response contracts plus API calls.
- `src/features/auth/state` owns the provider, auth hook, and refresh lifecycle.
- `src/features/auth/routing` owns protected-route and return-path validation.
- `src/features/auth/pages` owns route-level screens and their local form state.
- `App.tsx` composes routes; `main.tsx` mounts the provider inside the router.

These boundaries keep token lifecycle code out of forms and routing decisions
out of the API layer. Shared UI components remain presentation-only.

## Testing

Tests follow the existing Vitest and Testing Library conventions and cover:

- client validation and duplicate-submit prevention for each form;
- successful register, verification, login, forgot-password, and reset flows;
- field and page-level Problem Details presentation;
- the disabled Google affordance and inert callback page;
- startup loading without rendering protected children;
- startup refresh success and failure;
- scheduled refresh success and failure with fake timers;
- one in-flight refresh for concurrent triggers;
- safe and unsafe `returnTo` behavior; and
- the absence of auth-token writes to browser storage.

The frontend test suite, lint, formatting check, and production build are the
completion gates. Existing server code is unchanged, so server tests are not a
required gate for this frontend-only ticket.
