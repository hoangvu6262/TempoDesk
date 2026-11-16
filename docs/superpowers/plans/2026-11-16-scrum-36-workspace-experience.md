# SCRUM-36 Workspace Experience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the authenticated TempoDesk workspace shell, route-preserved workspace switcher, Team Workspace creation, invitation acceptance, and permission-aware member and invitation management UI.

**Architecture:** A shared `ApiProvider` owns the authenticated Axios client and its in-memory access token. `AuthProvider` owns authentication lifecycle, while a protected `WorkspaceProvider` owns accessible workspace data; the selected workspace is always derived from the route. Feature-local typed API adapters and permission helpers keep transport, product state, and presentation separate.

**Tech Stack:** React 19, TypeScript 6, React Router 8, Axios, Sass, existing TempoDesk UI kit, native accessible HTML controls.

## Global Constraints

- Add no frontend test files or test dependencies for SCRUM-36.
- Add no runtime dependencies; reuse React, React Router, Axios, Sass, Tabler Icons, and the existing UI kit.
- Follow `.cursor/rules/frontend-clean-code.mdc` and `.cursor/rules/frontend-ui.mdc`.
- Treat Twenty only as a structural and visual reference; do not copy `twenty-front` source.
- Use global SCSS with one `td-`-prefixed BEM block per component and semantic design tokens only.
- Keep access tokens, invitation tokens, selected workspace IDs, and authorization decisions out of browser storage.
- Use effective workspace roles for UI visibility only; backend 401/403/404/409 responses remain authoritative.
- Verify each checkpoint with formatting, lint, or build as specified before committing.

---

## File Map

- `client/web/src/lib/api/ApiContext.ts` — shared authenticated client context and hook.
- `client/web/src/lib/api/ApiProvider.tsx` — in-memory token, Axios client, and unauthorized callback ownership.
- `client/web/src/lib/api/api-client.ts` — single-flight 401 recovery and one-time request replay after API error normalization.
- `client/web/src/features/auth/state/AuthProvider.tsx` — refresh lifecycle, transient recovery, and terminal session expiry.
- `client/web/src/api/workspace-api.ts` — workspace, member, invitation types and HTTP operations.
- `client/web/src/features/workspaces/workspace-permissions.ts` — fixed role-based UI visibility helpers.
- `client/web/src/features/workspaces/workspace-errors.ts` — safe workspace validation and request messages.
- `client/web/src/features/workspaces/state/WorkspaceContext.ts` — workspace load-state contract and hook.
- `client/web/src/features/workspaces/state/WorkspaceProvider.tsx` — accessible workspace collection and in-memory selection.
- `client/web/src/features/workspaces/components/WorkspaceSwitcher.tsx` — native accessible workspace selection.
- `client/web/src/features/workspaces/components/WorkspaceSwitcher.scss` — switcher styling.
- `client/web/src/app/AuthenticatedAppShell.tsx` — protected workspace/account navigation shell.
- `client/web/src/app/AuthenticatedAppShell.scss` — responsive authenticated shell styling.
- `client/web/src/features/workspaces/routes/WorkspaceEntryPage.tsx` — `/app` workspace choice.
- `client/web/src/features/workspaces/routes/WorkspaceRoute.tsx` — accessible `:workspaceId` guard and outlet context.
- `client/web/src/features/workspaces/pages/WorkspaceOverviewPage.tsx` — minimal non-dashboard workspace landing state.
- `client/web/src/features/workspaces/pages/CreateWorkspacePage.tsx` — Team Workspace creation.
- `client/web/src/features/workspaces/pages/WorkspacePages.scss` — workspace page/form styling.
- `client/web/src/features/workspaces/pages/AcceptInvitationPage.tsx` — authenticated invitation acceptance.
- `client/web/src/features/workspaces/pages/WorkspaceMembersPage.tsx` — member/invitation loading and mutations.
- `client/web/src/features/workspaces/components/InviteMemberForm.tsx` — invite form.
- `client/web/src/features/workspaces/components/MemberList.tsx` — role, remove, and ownership controls.
- `client/web/src/features/workspaces/components/InvitationList.tsx` — pending invitation list and cancellation.
- `client/web/src/features/workspaces/components/WorkspaceManagement.scss` — management form/list styling.
- `client/web/src/routes/AppRoutes.tsx` — public routes plus protected nested workspace routes.
- `client/web/src/main.tsx` — `ApiProvider` placement.

---

### Task 1: Shared Authenticated API Runtime

**Files:**
- Create: `client/web/src/lib/api/ApiContext.ts`
- Create: `client/web/src/lib/api/ApiProvider.tsx`
- Modify: `client/web/src/lib/api/api-client.ts`
- Modify: `client/web/src/lib/api/index.ts`
- Modify: `client/web/src/features/auth/state/AuthProvider.tsx`
- Modify: `client/web/src/main.tsx`

**Interfaces:**
- Produces: `useApiRuntime(): { client: AxiosInstance; setAccessToken(value: string | null): void; setUnauthorizedHandler(handler: (() => void) | null): void }`.
- Changes: `createApiClient(baseUrl, requestHeaders?, onUnauthorized?)` calls `onUnauthorized` for normalized HTTP 401 errors.
- Preserves: the existing `AuthContextValue` and `useAuth().api` contract used by auth and session pages.

- [ ] **Step 1: Add the API runtime context**

Create a context with one strict hook. The public contract must be:

```ts
export type ApiRuntime = {
  client: AxiosInstance
  setAccessToken(value: string | null): void
  setUnauthorizedHandler(handler: (() => void) | null): void
}

export function useApiRuntime(): ApiRuntime
```

The hook throws when used outside `ApiProvider`.

- [ ] **Step 2: Create `ApiProvider` with module-stable runtime values**

Inside the provider, create the client exactly once with `useState`. Keep the token and unauthorized handler inside that initializer's closure:

```tsx
let accessToken: string | null = null
let unauthorizedHandler: (() => void) | null = null

const client = createApiClient(
  readClientConfig(import.meta.env).apiBaseUrl,
  () =>
    accessToken
      ? new Headers({ Authorization: `Bearer ${accessToken}` })
      : new Headers(),
  () => unauthorizedHandler?.(),
)
client.defaults.withCredentials = true
```

Return stable setters that update only those closure values; token changes must not render the React tree.

- [ ] **Step 3: Notify the auth boundary on 401**

Normalize the response error once, call the optional handler only when `apiError.status === 401`, then reject with that same `ApiError`:

```ts
const apiError = toApiError(error)
if (apiError.status === 401) onUnauthorized?.()
return Promise.reject(apiError)
```

- [ ] **Step 4: Refactor `AuthProvider` to consume the runtime**

Remove token/client creation from `createAuthRuntime`. Build `AuthenticationApi` from `client` unless the existing injected API prop is supplied. In `applyToken`, call `setAccessToken(result.accessToken)`; in `clearSession`, call `setAccessToken(null)`. Register `clearSession` through `setUnauthorizedHandler` in an effect and clear the handler on cleanup.

- [ ] **Step 5: Mount providers in dependency order**

In `main.tsx`, use:

```tsx
<ApiProvider>
  <AuthProvider>
    <App />
  </AuthProvider>
</ApiProvider>
```

Keep the existing `BrowserRouter`, theme, and error-boundary ordering otherwise unchanged.

- [ ] **Step 6: Verify and commit**

Run:

```bash
npm --prefix client/web run format:check
npm --prefix client/web run lint
npm --prefix client/web run build
```

Expected: all commands exit `0` and existing auth/session code compiles without contract changes.

Commit:

```bash
git add client/web/src/lib/api client/web/src/features/auth/state/AuthProvider.tsx client/web/src/main.tsx
git commit -m "refactor(frontend): share authenticated API runtime"
```

---

### Task 2: Workspace API Contract and Permission Policy

**Files:**
- Create: `client/web/src/api/workspace-api.ts`
- Create: `client/web/src/features/workspaces/workspace-permissions.ts`
- Create: `client/web/src/features/workspaces/workspace-errors.ts`

**Interfaces:**
- Consumes: authenticated `AxiosInstance` from Task 1.
- Produces: `createWorkspaceApi(client): WorkspaceApi` and fixed-role permission helpers.

- [ ] **Step 1: Define exact backend response types**

Use literal unions and DTO-shaped types:

```ts
export type WorkspaceType = 'personal' | 'team'
export type WorkspaceRole = 'owner' | 'admin' | 'member' | 'viewer'
export type ManageableWorkspaceRole = Exclude<WorkspaceRole, 'owner'>

export type Workspace = {
  id: string
  name: string
  type: WorkspaceType
  role: WorkspaceRole
}

export type WorkspaceMember = {
  id: string
  email: string
  role: WorkspaceRole
  joinedAt: string
}

export type WorkspaceInvitation = {
  id: string
  email: string
  role: ManageableWorkspaceRole
  createdAt: string
  expiresAt: string
}
```

- [ ] **Step 2: Implement every SCRUM-33/35 operation**

`WorkspaceApi` exposes:

```ts
listWorkspaces(): Promise<Workspace[]>
createWorkspace(name: string): Promise<Workspace>
listMembers(workspaceId: string): Promise<WorkspaceMember[]>
listInvitations(workspaceId: string): Promise<WorkspaceInvitation[]>
invite(workspaceId: string, email: string, role: ManageableWorkspaceRole): Promise<WorkspaceInvitation>
cancelInvitation(workspaceId: string, invitationId: string): Promise<void>
acceptInvitation(token: string): Promise<WorkspaceMember>
changeRole(workspaceId: string, memberId: string, role: ManageableWorkspaceRole): Promise<WorkspaceMember>
removeMember(workspaceId: string, memberId: string): Promise<void>
leave(workspaceId: string): Promise<void>
transferOwnership(workspaceId: string, memberId: string): Promise<void>
```

Use `encodeURIComponent` for every path ID and keep request bodies identical to backend DTO property names.

- [ ] **Step 3: Mirror the fixed permission matrix for UI visibility**

Export small predicates instead of one generic permission engine:

```ts
canInvite(actorRole, invitedRole)
canChangeRole(actorRole, currentRole, nextRole)
canRemoveMember(actorRole, targetRole)
canTransferOwnership(actorRole)
canLeaveWorkspace(role)
```

Owner manages Admin/Member/Viewer and transfers ownership. Admin manages only Member/Viewer. Member and Viewer have no destructive administration controls. Owner cannot leave and Owner targets cannot be changed or removed.

- [ ] **Step 4: Add feature-local API error readers**

Export `workspaceFieldError(error, fieldName)` for case-insensitive validation fields and `workspaceRequestError(error, fallback)` for safe `ApiError` detail handling. Neither helper may expose response bodies, tokens, or transport objects.

- [ ] **Step 5: Verify and commit**

Run `npm --prefix client/web run build` and expect exit `0`.

Commit:

```bash
git add client/web/src/api/workspace-api.ts client/web/src/features/workspaces/workspace-permissions.ts client/web/src/features/workspaces/workspace-errors.ts
git commit -m "feat(frontend): add workspace API contract"
```

---

### Task 3: Accessible Workspace State

**Files:**
- Create: `client/web/src/features/workspaces/state/WorkspaceContext.ts`
- Create: `client/web/src/features/workspaces/state/WorkspaceProvider.tsx`

**Interfaces:**
- Consumes: `createWorkspaceApi(client)` and authenticated status.
- Produces: `useWorkspaces(): WorkspaceContextValue`.

- [ ] **Step 1: Define one explicit load-state contract**

```ts
export type WorkspaceLoadState =
  | { status: 'loading'; workspaces: Workspace[] }
  | { status: 'error'; workspaces: Workspace[]; message: string }
  | { status: 'ready'; workspaces: Workspace[] }

export type WorkspaceContextValue = {
  state: WorkspaceLoadState
  lastSelectedWorkspaceId: string | null
  api: WorkspaceApi
  refreshWorkspaces(): Promise<Workspace[]>
  addWorkspace(workspace: Workspace): void
  selectWorkspace(workspaceId: string): void
}
```

- [ ] **Step 2: Load accessible workspaces after protected auth resolves**

Create `WorkspaceApi` once with `useMemo`. On mount, list workspaces, ignore stale completion after cleanup, and map failures to the fixed message `Unable to load your workspaces. Please try again.`. Do not read or write storage. Render `<Outlet />` inside the context provider so the component can be used directly as the protected pathless route element in Task 5.

- [ ] **Step 3: Implement stable provider operations**

- `refreshWorkspaces` sets loading only when no confirmed data exists, returns the fetched list, and retains confirmed data if a refresh fails.
- `addWorkspace` appends only when its ID is not already present and sets it as the last selected ID.
- `selectWorkspace` records the ID only when it exists in the current accessible list.

- [ ] **Step 4: Verify and commit**

Run `npm --prefix client/web run lint` and `npm --prefix client/web run build`; expect both to exit `0`.

Commit:

```bash
git add client/web/src/features/workspaces/state
git commit -m "feat(frontend): load accessible workspaces"
```

---

### Task 4: Authenticated Shell and Workspace Switcher

**Files:**
- Create: `client/web/src/features/workspaces/components/WorkspaceSwitcher.tsx`
- Create: `client/web/src/features/workspaces/components/WorkspaceSwitcher.scss`
- Create: `client/web/src/app/AuthenticatedAppShell.tsx`
- Create: `client/web/src/app/AuthenticatedAppShell.scss`

**Interfaces:**
- Consumes: `useWorkspaces`, `useLocation`, `useNavigate`, and `useAuth`.
- Produces: `<AuthenticatedAppShell />` with nested `<Outlet />`.

- [ ] **Step 1: Build the native accessible switcher**

`WorkspaceSwitcherProps` contains `workspaces`, `selectedWorkspaceId`, `loading`, `error`, `disabled`, `onSelect`, and `onRetry`. Render a labeled native `select` with Personal and Team `optgroup` sections. Render retry feedback through `Alert`; do not put the Team creation action inside the select.

The select handler passes only the selected ID:

```tsx
onChange={(event) => onSelect(event.target.value)}
```

- [ ] **Step 2: Build the protected shell**

Use `useMatch('/app/workspaces/:workspaceId/*')` to derive the current route ID. On selection, call `selectWorkspace(id)` and navigate to `/app/workspaces/${encodeURIComponent(id)}`. Include links for the selected workspace overview, Create Team Workspace, and Account Sessions. Hide workspace-specific navigation when no accessible workspace is selected. Do not add sign-out because the current backend contract has no logout operation that revokes the refresh cookie.

- [ ] **Step 3: Style with TempoDesk tokens**

Create a compact sidebar/header/content shell inspired by Twenty. Use only existing CSS variables. At narrow width, stack the navigation above content and keep the native select full width. Preserve the skip link and `main#main` target.

- [ ] **Step 4: Verify and commit**

Run format check, lint, and build; expect all to exit `0`.

Commit:

```bash
git add client/web/src/app/AuthenticatedAppShell.* client/web/src/features/workspaces/components/WorkspaceSwitcher.*
git commit -m "feat(frontend): add authenticated workspace shell"
```

---

### Task 5: Protected Workspace Routes and Team Creation

**Files:**
- Create: `client/web/src/features/workspaces/routes/WorkspaceEntryPage.tsx`
- Create: `client/web/src/features/workspaces/routes/WorkspaceRoute.tsx`
- Create: `client/web/src/features/workspaces/pages/WorkspaceOverviewPage.tsx`
- Create: `client/web/src/features/workspaces/pages/CreateWorkspacePage.tsx`
- Create: `client/web/src/features/workspaces/pages/WorkspacePages.scss`
- Modify: `client/web/src/routes/AppRoutes.tsx`

**Interfaces:**
- Produces: `useSelectedWorkspace(): Workspace` from route outlet context.
- Consumes: `AuthenticatedAppShell`, `WorkspaceProvider`, and `SessionManagementPage`.

- [ ] **Step 1: Implement `/app` selection without an effect**

Return loading/error feedback until workspace state resolves. When ready, choose:

```ts
const destination =
  validLastSelected ?? personalWorkspace ?? state.workspaces[0] ?? null
```

Render `<Navigate replace>` when a destination exists and an explicit empty state otherwise.

- [ ] **Step 2: Guard route workspace access**

`WorkspaceRoute` reads `workspaceId`, waits for the list, and finds an exact ID match. When missing after a ready load, render `<Navigate to="/app" replace />`. When present, call `selectWorkspace` once in an effect and render `<Outlet context={{ workspace }} />`. `useSelectedWorkspace` reads that exact context.

- [ ] **Step 3: Add the non-dashboard workspace landing page**

Show workspace name, Personal/Team label, and effective role. Do not add dashboard widgets, usage, billing, branding, or analytics.

- [ ] **Step 4: Add Team Workspace creation**

Use a controlled `name`, trim before submit, and reject blank or over-100-character values locally. Submit through `api.createWorkspace`, call `addWorkspace`, then navigate with `replace` to the new workspace route. Map backend `name` validation with `fieldError`; show conflict/network feedback through `Alert`.

- [ ] **Step 5: Replace the single `/app` route with nested routes**

Keep public routes unchanged. The protected tree is:

```tsx
<Route element={<ProtectedRoute><WorkspaceProvider /></ProtectedRoute>}>
  <Route path="/app" element={<AuthenticatedAppShell />}>
    <Route index element={<WorkspaceEntryPage />} />
    <Route path="workspaces/new" element={<CreateWorkspacePage />} />
    <Route path="workspaces/:workspaceId" element={<WorkspaceRoute />}>
      <Route index element={<WorkspaceOverviewPage />} />
    </Route>
    <Route path="account/sessions" element={<SessionManagementPage />} />
  </Route>
</Route>
```

- [ ] **Step 6: Verify and commit**

Run format check, lint, and build; expect all to exit `0`.

Commit:

```bash
git add client/web/src/routes/AppRoutes.tsx client/web/src/features/workspaces/routes client/web/src/features/workspaces/pages/WorkspaceOverviewPage.tsx client/web/src/features/workspaces/pages/CreateWorkspacePage.tsx client/web/src/features/workspaces/pages/WorkspacePages.scss
git commit -m "feat(frontend): route workspace context and creation"
```

---

### Task 6: Invitation Acceptance

**Files:**
- Create: `client/web/src/features/workspaces/pages/AcceptInvitationPage.tsx`
- Modify: `client/web/src/features/workspaces/pages/WorkspacePages.scss`
- Modify: `client/web/src/routes/AppRoutes.tsx`

**Interfaces:**
- Consumes: `useApiRuntime`, `createWorkspaceApi`, `ProtectedRoute`, and query parameter `token`.

- [ ] **Step 1: Build the protected acceptance page**

Read the token once from `new URLSearchParams(location.search).get('token')`. Never copy it to storage. A missing token immediately renders the generic invalid invitation message used by the backend contract.

For a present token, submit automatically once after authentication. Show loading status, generic invalid/expired feedback for HTTP 400, conflict feedback for 409, permission feedback for 403, and retryable network feedback otherwise.

- [ ] **Step 2: Remove the token after success**

On success navigate with:

```ts
navigate('/app', { replace: true })
```

The newly mounted `WorkspaceProvider` fetches the now-accessible workspace list. Do not include the token in success copy, logs, or error details.

- [ ] **Step 3: Add the authenticated route outside the workspace shell**

Add `/accept-invitation` wrapped by `ProtectedRoute` but not `WorkspaceProvider` or `AuthenticatedAppShell`. This preserves the existing login `returnTo` query flow without requiring existing workspace access.

- [ ] **Step 4: Verify and commit**

Run lint and build; expect both to exit `0`.

Commit:

```bash
git add client/web/src/features/workspaces/pages/AcceptInvitationPage.tsx client/web/src/features/workspaces/pages/WorkspacePages.scss client/web/src/routes/AppRoutes.tsx
git commit -m "feat(frontend): accept workspace invitations"
```

---

### Task 7: Invitation Management

**Files:**
- Create: `client/web/src/features/workspaces/components/InviteMemberForm.tsx`
- Create: `client/web/src/features/workspaces/components/InvitationList.tsx`
- Create: `client/web/src/features/workspaces/components/WorkspaceManagement.scss`
- Create: `client/web/src/features/workspaces/pages/WorkspaceMembersPage.tsx`
- Modify: `client/web/src/app/AuthenticatedAppShell.tsx`
- Modify: `client/web/src/routes/AppRoutes.tsx`

**Interfaces:**
- Consumes: selected `Workspace`, `WorkspaceApi`, `canInvite`, and existing form/feedback UI.
- Produces: the real members route page used by Task 5.

- [ ] **Step 1: Build the controlled invite form**

Props contain `allowedRoles`, `loading`, and `onSubmit(email, role)`. Use existing `validateEmail`, a native labeled role select, and `Button`. Do not offer Owner. Owner may choose Admin/Member/Viewer; Admin may choose Member/Viewer; other roles do not render the form.

- [ ] **Step 2: Build pending invitation presentation**

Render semantic list items with email, role, and expiry `<time>`. Show Cancel only when `canInvite(actorRole, invitation.role)` is true. Confirm cancellation with `window.confirm`, disable all row mutations while one is pending, and pass the invitation ID to the page callback.

- [ ] **Step 3: Load members and invitations in parallel**

In `WorkspaceMembersPage`, use:

```ts
const [members, invitations] = await Promise.all([
  api.listMembers(workspace.id),
  api.listInvitations(workspace.id),
])
```

Ignore stale completion when the route workspace changes. Distinguish initial loading, load error, empty invitations, and confirmed lists.

- [ ] **Step 4: Implement invite and cancellation mutations**

Append the invitation returned by `invite`. Remove the cancelled ID after a successful no-content response. Map field validation, 403, 409, and network failures to specific accessible messages. On 404, refresh accessible workspaces and navigate to `/app`.

- [ ] **Step 5: Expose the members route and navigation**

Add the `members` child below `workspaces/:workspaceId` in `AppRoutes.tsx`. Add a Members navigation link to `AuthenticatedAppShell` only when the selected workspace route is active.

- [ ] **Step 6: Verify and commit**

Run format check, lint, and build; expect all to exit `0`.

Commit:

```bash
git add client/web/src/app/AuthenticatedAppShell.tsx client/web/src/routes/AppRoutes.tsx client/web/src/features/workspaces/components/InviteMemberForm.tsx client/web/src/features/workspaces/components/InvitationList.tsx client/web/src/features/workspaces/components/WorkspaceManagement.scss client/web/src/features/workspaces/pages/WorkspaceMembersPage.tsx
git commit -m "feat(frontend): manage workspace invitations"
```

---

### Task 8: Member Roles, Removal, Leaving, and Ownership Transfer

**Files:**
- Create: `client/web/src/features/workspaces/components/MemberList.tsx`
- Modify: `client/web/src/features/workspaces/pages/WorkspaceMembersPage.tsx`
- Modify: `client/web/src/features/workspaces/components/WorkspaceManagement.scss`

**Interfaces:**
- Consumes: Task 2 permission predicates and Task 7 member state.

- [ ] **Step 1: Render permission-aware member controls**

Each member row shows email, role, and joined time. Owner targets have no role/remove controls. Admin actors have controls only for Member/Viewer targets and may select only Member/Viewer. Owner actors may select Admin/Member/Viewer for non-Owner targets. Member and Viewer actors see no destructive administration controls.

- [ ] **Step 2: Implement role changes with escalation confirmation**

When the next role is Admin and the current role is not Admin, require `window.confirm`. Call `changeRole` and replace the returned member by ID. Disable row actions while pending and announce success. A 403 keeps the confirmed row unchanged and shows a permission alert.

- [ ] **Step 3: Implement removal**

Require confirmation containing the target email, call `removeMember`, and remove the row only after success. Never render removal for Owner or a target disallowed by `canRemoveMember`.

- [ ] **Step 4: Implement ownership transfer**

Render transfer only for an Owner actor and non-Owner target. Confirm that the current Owner becomes Admin, call `transferOwnership`, then refresh both workspace and member collections. Use the refreshed effective role rather than predicting the final state locally.

- [ ] **Step 5: Implement leave**

Render Leave only for Team Workspaces when `canLeaveWorkspace(workspace.role)` is true. Confirm, call `leave`, refresh accessible workspaces, and navigate to `/app` with `replace`. Personal Workspace and Owner never show Leave.

- [ ] **Step 6: Handle concurrent authorization changes**

For every mutation, preserve confirmed UI state on failure. On 403 show `You no longer have permission to perform this action.` and refresh workspaces. On 409 show action-specific conflict guidance. On 404 refresh and return to `/app`.

- [ ] **Step 7: Verify and commit**

Run format check, lint, and build; expect all to exit `0`.

Commit:

```bash
git add client/web/src/features/workspaces/components/MemberList.tsx client/web/src/features/workspaces/components/WorkspaceManagement.scss client/web/src/features/workspaces/pages/WorkspaceMembersPage.tsx
git commit -m "feat(frontend): manage workspace members"
```

---

### Task 9: Final Integration and Manual Verification

**Files:**
- Modify only files required by verification findings in `client/web/src/`.

**Interfaces:**
- Consumes: the complete SCRUM-36 frontend slice.
- Produces: a buildable, lint-clean, format-clean authenticated workspace experience.

- [ ] **Step 1: Run the full frontend gates**

```bash
npm --prefix client/web run format:check
npm --prefix client/web run lint
npm --prefix client/web run build
```

Expected: all three commands exit `0`.

- [ ] **Step 2: Run a source safety scan**

```bash
rg -n "localStorage|sessionStorage" client/web/src/features/workspaces client/web/src/api/workspace-api.ts
rg -n "token" client/web/src/features/workspaces/pages/AcceptInvitationPage.tsx
```

Expected: the storage scan returns no matches; token matches are limited to URL parsing and the acceptance request, with no logging or persistence.

- [ ] **Step 3: Verify routes and behavior manually**

Run `npm --prefix client/web run dev` with the local API and verify:

- keyboard-only workspace selection and visible focus;
- Personal versus Team labels;
- refresh/deep link on a valid workspace route;
- inaccessible workspace route returns to `/app`;
- Team creation selects the returned Owner workspace;
- invitation acceptance survives login and removes the token with history replacement;
- Owner/Admin/Member/Viewer action visibility;
- invite, cancel, role change, remove, leave, and transfer confirmations;
- 401 sign-out, 403 permission feedback, 409 conflict feedback, and network retry;
- `/app/account/sessions` remains reachable; and
- narrow layout plus light and dark themes.

- [ ] **Step 4: Commit verification fixes if any**

If verification required code changes:

```bash
git add client/web/src
git commit -m "fix(frontend): complete SCRUM-36 verification"
```

If no files changed, do not create an empty commit.

---

### Task 10: Recover Expired Access Tokens Without Losing UI State

**Files:**
- Modify: `client/web/src/lib/api/ApiContext.ts`
- Modify: `client/web/src/lib/api/ApiProvider.tsx`
- Modify: `client/web/src/lib/api/api-client.ts`
- Modify: `client/web/src/features/auth/state/AuthContext.ts`
- Modify: `client/web/src/features/auth/state/AuthProvider.tsx`
- Modify: `client/web/src/routes/ProtectedRoute.tsx`

**Interfaces:**
- Changes: `setUnauthorizedHandler(handler: (() => Promise<boolean>) | null)` resolves `true` only after a new access token is installed.
- Changes: `AuthStatus` adds `unavailable` for an initial session check blocked by a transient failure.
- Produces: `retrySession(): Promise<void>` for the protected connection-error state.

- [ ] **Step 1: Make the shared client recover and replay once**

When a non-refresh request receives HTTP 401, call the asynchronous unauthorized
handler. Concurrent calls share the handler's in-flight refresh promise. If it
resolves `true`, remove the stale Authorization header and replay the original
request once. Mark the request before replay so a second 401 is returned without
another recovery attempt. Never recover `/api/auth/refresh` from its own 401.

- [ ] **Step 2: Distinguish terminal and transient refresh failures**

Make `AuthProvider.runRefresh()` resolve `true` after applying a new token and
`false` after an error. Clear authentication only for `ApiError.status === 401`.
For network, timeout, invalid-response, and 5xx failures, retain an existing
authenticated state and schedule another refresh with bounded backoff. If no
access token exists during initial application startup, expose `unavailable`
instead of redirecting to login.

- [ ] **Step 3: Keep protected content safe during recovery**

Register `runRefresh` as the unauthorized handler. Add `retrySession` to the auth
context. `ProtectedRoute` renders an accessible connection-error alert with a
retry button for `unavailable`; it redirects only for `anonymous`, after the
refresh endpoint has confirmed HTTP 401.

- [ ] **Step 4: Verify and commit**

Run:

```bash
npm --prefix client/web run format:check
npm --prefix client/web run lint
npm --prefix client/web run build
```

Expected: all commands exit `0`. Do not add frontend tests by the explicit
SCRUM-36 product decision.

Commit:

```bash
git add docs/superpowers client/web/src/lib/api client/web/src/features/auth/state client/web/src/routes/ProtectedRoute.tsx
git commit -m "fix(frontend): recover expired access tokens"
```
