# SCRUM-29 Device Session Management UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a protected TempoDesk page that lists account sessions, identifies the current browser session authoritatively, and lets the user revoke one or all sessions with clear feedback.

**Architecture:** Extend the existing session list contract with a server-computed `isCurrent` flag derived from the HttpOnly refresh cookie, then add session operations to the existing frontend authentication adapter. A focused React feature owns list/mutation state and renders inside the existing protected `/app` shell using only the current UI kit and design tokens.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10, React 19, TypeScript 6, Axios, Sass, existing TempoDesk UI kit.

## Global Constraints

- Keep access tokens in provider memory and refresh tokens in the server-managed HttpOnly cookie.
- Never render, log, or return access tokens, refresh tokens, or token hashes.
- Derive the current session on the server; never infer it from list order or activity time.
- Keep `/app` protected and use it for the session page; do not add a settings shell.
- Add no dependency, global state library, query library, user-agent parser, or modal framework.
- Use native confirmation dialogs for destructive actions and live regions for results.
- Preserve the existing API ownership checks and revoke-all behavior, which includes the current session.
- Do not restore the test projects removed by the repository baseline.
- Do not stage or modify the unrelated deletion of `scripts/check-vulnerabilities.mjs`.

---

## File map

- Modify `server/src/TempoDesk.Application/Authentication/Session/AuthenticationSession.cs` to expose `IsCurrent` internally.
- Modify `server/src/TempoDesk.Application/Authentication/Session/IAuthenticationSessionService.cs` so listing receives the current raw refresh cookie.
- Modify `server/src/TempoDesk.Application/Authentication/RefreshToken/IRefreshTokenRepository.cs` so persistence receives only a bounded hash.
- Modify `server/src/TempoDesk.Infrastructure/Identity/Session/AuthenticationSessionService.cs` to validate and hash the cookie.
- Modify `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshTokenRepository.cs` to mark the matching active family.
- Modify `server/src/TempoDesk.Api/Features/Authentication/Session/SessionResponseDto.cs` and `SessionsController.cs` to return `isCurrent`.
- Modify `client/web/src/api/auth-api.ts` with session DTOs and API operations.
- Create `client/web/src/features/sessions/session-display.ts` for pure display formatting.
- Create `client/web/src/features/sessions/SessionManagementPage.tsx` for data loading and mutations.
- Create `client/web/src/features/sessions/SessionManagementPage.scss` for responsive presentation.
- Modify `client/web/src/routes/AppRoutes.tsx` to mount the new page.
- Delete `client/web/src/app/AuthenticatedHome.tsx`, whose placeholder is replaced.

### Task 1: Authoritative current-session API contract

**Files:**
- Modify: `server/src/TempoDesk.Application/Authentication/Session/AuthenticationSession.cs`
- Modify: `server/src/TempoDesk.Application/Authentication/Session/IAuthenticationSessionService.cs`
- Modify: `server/src/TempoDesk.Application/Authentication/RefreshToken/IRefreshTokenRepository.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/Session/AuthenticationSessionService.cs`
- Modify: `server/src/TempoDesk.Infrastructure/Identity/RefreshToken/RefreshTokenRepository.cs`
- Modify: `server/src/TempoDesk.Api/Features/Authentication/Session/SessionResponseDto.cs`
- Modify: `server/src/TempoDesk.Api/Features/Authentication/Session/SessionsController.cs`

**Interfaces:**
- Consumes: `RefreshTokenCookie.Read(Request)` and authenticated `Guid userId`.
- Produces: `AuthenticationSession(..., AuthenticationSessionStatus Status, bool IsCurrent)`.
- Produces: `ListAsync(Guid userId, string rawToken, CancellationToken cancellationToken = default)`.
- Produces: `ListSessionsAsync(Guid userId, string? currentTokenHash, DateTimeOffset now, CancellationToken cancellationToken = default)`.
- Produces: JSON session items with camel-case `isCurrent`.

- [ ] **Step 1: Confirm the existing server solution builds before the contract change**

Run:

```bash
dotnet build server/TempoDesk.sln --no-restore
```

Expected: exit code 0. If restore assets are missing, run the same command without `--no-restore`; request network permission only if package restore requires it.

- [ ] **Step 2: Add `IsCurrent` to the application session record**

Change `AuthenticationSession` to:

```csharp
public sealed record AuthenticationSession(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    AuthenticationSessionStatus Status,
    bool IsCurrent);
```

- [ ] **Step 3: Carry the current cookie through the application boundary**

Change the list service signature to:

```csharp
Task<IReadOnlyList<AuthenticationSession>> ListAsync(
    Guid userId,
    string rawToken,
    CancellationToken cancellationToken = default);
```

Change the repository signature to:

```csharp
Task<IReadOnlyList<AuthenticationSession>> ListSessionsAsync(
    Guid userId,
    string? currentTokenHash,
    DateTimeOffset now,
    CancellationToken cancellationToken = default);
```

In `AuthenticationSessionService`, implement listing with the same token bound used by logout:

```csharp
public Task<IReadOnlyList<AuthenticationSession>> ListAsync(
    Guid userId,
    string rawToken,
    CancellationToken cancellationToken = default) =>
    refreshTokens.ListSessionsAsync(
        userId,
        string.IsNullOrEmpty(rawToken) || rawToken.Length > 512
            ? null
            : Hash(rawToken),
        timeProvider.GetUtcNow(),
        cancellationToken);
```

- [ ] **Step 4: Mark only the active matching family in persistence**

Include `token.TokenHash` in the anonymous row selected by
`ListSessionsAsync`, accept `currentTokenHash` before `now`, and append this
argument when constructing each `AuthenticationSession`:

```csharp
currentTokenHash is not null && family.Any(row =>
    row.TokenHash == currentTokenHash &&
    row.RevokedAt is null &&
    row.ExpiresAt > now)
```

This keeps raw tokens outside persistence and ensures stale, revoked, expired,
foreign, or missing cookie values never mark a row current.

- [ ] **Step 5: Expose the flag from the HTTP controller**

Pass the cookie when listing:

```csharp
var result = await sessions.ListAsync(
    UserId(), RefreshTokenCookie.Read(Request), cancellationToken);
```

Append `session.IsCurrent` to the `SessionResponseDto` construction and change
the DTO record to:

```csharp
public sealed record SessionResponseDto(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    bool IsCurrent);
```

- [ ] **Step 6: Verify all contract callers and build the server**

Run:

```bash
rg -n "ListAsync\(|ListSessionsAsync\(|new AuthenticationSession\(" server/src -g '*.cs'
dotnet build server/TempoDesk.sln --no-restore
```

Expected: the only session-list caller is `SessionsController`; every signature agrees; build exits 0 with no errors.

- [ ] **Step 7: Commit the backend slice**

```bash
git add server/src/TempoDesk.Application/Authentication server/src/TempoDesk.Infrastructure/Identity server/src/TempoDesk.Api/Features/Authentication/Session
git commit -m "feat(auth): identify the current session"
```

### Task 2: Frontend session API and display helpers

**Files:**
- Modify: `client/web/src/api/auth-api.ts`
- Create: `client/web/src/features/sessions/session-display.ts`

**Interfaces:**
- Produces: `SessionStatus = 'active' | 'revoked' | 'expired'`.
- Produces: `AuthenticationSession` matching the API response.
- Produces: `AuthenticationApi.listSessions()`, `revokeSession(id)`, and `revokeAllSessions()`.
- Produces: `describeDevice(userAgent: string | null): string`.
- Produces: `formatSessionDate(value: string): string`.

- [ ] **Step 1: Add the session DTO and adapter signatures**

Add above `AuthenticationApi`:

```ts
export type SessionStatus = 'active' | 'revoked' | 'expired'

export type AuthenticationSession = {
  id: string
  userAgent: string | null
  ipAddress: string | null
  createdAt: string
  lastUsedAt: string
  expiresAt: string
  status: SessionStatus
  isCurrent: boolean
}
```

Append to `AuthenticationApi`:

```ts
listSessions(): Promise<AuthenticationSession[]>
revokeSession(id: string): Promise<void>
revokeAllSessions(): Promise<void>
```

- [ ] **Step 2: Implement the three HTTP operations**

Append to the object returned by `createAuthenticationApi`:

```ts
listSessions: async () =>
  (await client.get<AuthenticationSession[]>('/api/auth/sessions')).data,
revokeSession: async (id) => {
  await client.delete(`/api/auth/sessions/${encodeURIComponent(id)}`)
},
revokeAllSessions: async () => {
  await client.delete('/api/auth/sessions')
},
```

- [ ] **Step 3: Add conservative device and date formatting helpers**

Create `session-display.ts`:

```ts
const BROWSERS: Array<[RegExp, string]> = [
  [/Edg\//, 'Microsoft Edge'],
  [/OPR\//, 'Opera'],
  [/Chrome\//, 'Chrome'],
  [/Firefox\//, 'Firefox'],
  [/Safari\//, 'Safari'],
]

const PLATFORMS: Array<[RegExp, string]> = [
  [/iPhone/, 'iPhone'],
  [/iPad/, 'iPad'],
  [/Android/, 'Android'],
  [/Windows/, 'Windows'],
  [/Macintosh|Mac OS X/, 'macOS'],
  [/Linux/, 'Linux'],
]

export function describeDevice(userAgent: string | null) {
  if (!userAgent) return 'Unknown device'
  const browser = BROWSERS.find(([pattern]) => pattern.test(userAgent))?.[1]
  const platform = PLATFORMS.find(([pattern]) => pattern.test(userAgent))?.[1]
  if (browser && platform) return `${browser} on ${platform}`
  return browser ?? platform ?? 'Unknown device'
}

export function formatSessionDate(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? 'Unknown'
    : new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(date)
}
```

- [ ] **Step 4: Verify the frontend contracts compile and format**

Run:

```bash
cd client/web
npm run build
npx prettier --check src/api/auth-api.ts src/features/sessions/session-display.ts
```

Expected: both commands exit 0.

- [ ] **Step 5: Commit the adapter slice**

```bash
git add client/web/src/api/auth-api.ts client/web/src/features/sessions/session-display.ts
git commit -m "feat(web): add session management API"
```

### Task 3: Session management page

**Files:**
- Create: `client/web/src/features/sessions/SessionManagementPage.tsx`
- Create: `client/web/src/features/sessions/SessionManagementPage.scss`
- Modify: `client/web/src/routes/AppRoutes.tsx`
- Delete: `client/web/src/app/AuthenticatedHome.tsx`

**Interfaces:**
- Consumes: `useAuth()` returning `api`, `status`, and `logoutLocal()`.
- Consumes: `AuthenticationSession`, `describeDevice`, and `formatSessionDate`.
- Produces: `SessionManagementPage` mounted at protected `/app`.

- [ ] **Step 1: Create the page state and initial list loading**

Create `SessionManagementPage.tsx` with imports for React hooks, the existing
`Alert`, `Button`, and `Spinner`, `useAuth`, the session type, both display
helpers, and its SCSS. Define these states:

```tsx
const { api, logoutLocal } = useAuth()
const [sessions, setSessions] = useState<AuthenticationSession[] | null>(null)
const [loadError, setLoadError] = useState<string | null>(null)
const [actionError, setActionError] = useState<string | null>(null)
const [success, setSuccess] = useState<string | null>(null)
const [pendingId, setPendingId] = useState<string | null>(null)
const [revokingAll, setRevokingAll] = useState(false)
```

Implement a stable loader:

```tsx
const loadSessions = useCallback(async () => {
  setLoadError(null)
  try {
    setSessions(await api.listSessions())
  } catch {
    setLoadError('Unable to load your sessions. Please try again.')
  }
}, [api])

useEffect(() => {
  void loadSessions()
}, [loadSessions])
```

- [ ] **Step 2: Implement revoke-one and revoke-all behavior**

Add handlers:

```tsx
async function revokeSession(session: AuthenticationSession) {
  if (!window.confirm(
    session.isCurrent
      ? 'Revoke this session? You will be signed out on this device.'
      : 'Revoke this session? That device will no longer be able to refresh its login.',
  )) return

  setPendingId(session.id)
  setActionError(null)
  setSuccess(null)
  try {
    await api.revokeSession(session.id)
    if (session.isCurrent) {
      logoutLocal()
      return
    }
    setSessions((current) => current?.map((item) =>
      item.id === session.id ? { ...item, status: 'revoked' } : item,
    ) ?? null)
    setSuccess('Session revoked successfully.')
  } catch {
    setActionError('Unable to revoke the session. Please try again.')
  } finally {
    setPendingId(null)
  }
}

async function revokeAllSessions() {
  if (!window.confirm(
    'Revoke all sessions? You will be signed out on every device, including this one.',
  )) return

  setRevokingAll(true)
  setActionError(null)
  setSuccess(null)
  try {
    await api.revokeAllSessions()
    logoutLocal()
  } catch {
    setActionError('Unable to revoke all sessions. Please try again.')
    setRevokingAll(false)
  }
}
```

- [ ] **Step 3: Render loading, error, empty, and session-list states**

Render a page header with `Manage sessions` and a danger `Revoke all sessions`
button disabled when no active session exists or a mutation is pending. Then
render:

```tsx
{loadError && (
  <Alert variant="error" role="alert" title="Sessions unavailable">
    <p>{loadError}</p>
    <Button variant="secondary" size="small" onClick={() => void loadSessions()}>
      Try again
    </Button>
  </Alert>
)}
{actionError && <Alert variant="error" role="alert">{actionError}</Alert>}
{success && <Alert variant="success" role="status">{success}</Alert>}
{sessions === null && !loadError && (
  <div className="td-sessions__loading" role="status">
    <Spinner /> Loading sessions…
  </div>
)}
{sessions?.length === 0 && (
  <div className="td-sessions__empty">
    <h2>No sessions found</h2>
    <p>There are no login sessions available for this account.</p>
  </div>
)}
```

For every row render:

- `describeDevice(session.userAgent)` as its heading;
- `Current session` and status badges;
- IP when present;
- `<time dateTime={...}>{formatSessionDate(...)}</time>` for created, last
  activity, and expiry;
- a danger `Revoke` button only when `status === 'active'`, with
  `loading={pendingId === session.id}` and all destructive actions disabled
  while any mutation is pending.

- [ ] **Step 4: Add responsive, token-based styles**

Create `SessionManagementPage.scss` using the `td-sessions` block. Implement:

- a flex page header that stacks below 640px;
- a bordered `var(--color-surface)` list with one row per session;
- a responsive metadata grid using `repeat(auto-fit, minmax(9rem, 1fr))`;
- muted labels and secondary text;
- distinct current, active, revoked, and expired badges using existing color
  tokens;
- visible spacing around alerts, loading, and empty states; and
- a row action that remains reachable in normal keyboard order.

Do not add literal colors, animations, or selectors outside the feature block.

- [ ] **Step 5: Mount the page and remove the placeholder**

In `AppRoutes.tsx`, replace:

```tsx
import { AuthenticatedHome } from '../app/AuthenticatedHome'
```

with:

```tsx
import { SessionManagementPage } from '../features/sessions/SessionManagementPage'
```

Replace `<AuthenticatedHome />` with `<SessionManagementPage />` and delete
`client/web/src/app/AuthenticatedHome.tsx`.

- [ ] **Step 6: Run frontend quality gates**

Run:

```bash
cd client/web
npm run build
npm run lint
npm run format:check
```

Expected: all commands exit 0. If formatting alone fails, run
`npx prettier --write` only on the SCRUM-29 files, then repeat all three gates.

- [ ] **Step 7: Commit the page slice**

```bash
git add client/web/src/features/sessions client/web/src/routes/AppRoutes.tsx client/web/src/app/AuthenticatedHome.tsx
git commit -m "feat(web): manage device sessions"
```

### Task 4: Integrated verification and handoff

**Files:**
- Inspect: all files changed since the design commit.

**Interfaces:**
- Consumes: completed backend and frontend slices.
- Produces: verified SCRUM-29 branch with no unrelated staged changes.

- [ ] **Step 1: Run final build and static checks**

Run:

```bash
dotnet build server/TempoDesk.sln --no-restore
cd client/web
npm run build
npm run lint
npm run format:check
```

Expected: every command exits 0.

- [ ] **Step 2: Review security-sensitive data flow**

Run:

```bash
rg -n "accessToken|refreshToken|tokenHash|TokenHash|localStorage|sessionStorage" client/web/src/features/sessions server/src/TempoDesk.Api/Features/Authentication/Session
rg -n "isCurrent|IsCurrent|currentTokenHash" client/web/src server/src
```

Expected: the first command finds no token or browser-storage handling in the
new UI/API response; the second shows the flag traveling consistently from the
repository to the rendered current-session badge.

- [ ] **Step 3: Inspect the final diff and working tree**

Run:

```bash
git diff --check development...HEAD
git diff --stat development...HEAD
git status --short
```

Expected: no whitespace errors; only SCRUM-29 commits are on the branch; the
pre-existing deletion of `scripts/check-vulnerabilities.mjs` remains unstaged
and otherwise untouched.

- [ ] **Step 4: Perform browser verification when the local stack is available**

Verify with keyboard-only interaction:

1. Startup shows a labelled session-list loading state.
2. The current browser has exactly one `Current session` badge.
3. A second active session can be revoked after confirmation and updates to
   `Revoked` immediately.
4. Cancelling either confirmation makes no request and changes no state.
5. Revoking the current session redirects to login.
6. Revoke-all warns that the current device is included, then redirects to
   login after success.
7. Failed list and mutation requests show retryable, announced errors.
8. No response or DOM text exposes a token.

If the database/API environment is unavailable, report browser verification as
not run rather than claiming it passed.
