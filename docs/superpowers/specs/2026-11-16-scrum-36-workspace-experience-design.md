# SCRUM-36 Authenticated Workspace Experience Design

## Goal

Build the authenticated application shell, workspace switcher, Team Workspace
creation flow, and workspace member and invitation management UI. The frontend
uses effective workspace roles for usability while treating backend
authorization and HTTP responses as authoritative.

## Scope

The change adds:

- an authenticated shell with workspace and account navigation;
- accessible workspace selection and route-based workspace context;
- Team Workspace creation;
- member and pending-invitation management;
- invitation acceptance from the existing email link;
- role changes, invitation cancellation, removal, leaving, and ownership
  transfer with confirmation and feedback; and
- the existing session management page at `/app/account/sessions`.

Dashboards, workspace branding, billing, usage limits, custom roles, and new
backend authorization behavior remain out of scope. No frontend automated tests
are added for this ticket by product decision.

## Architecture

### Authenticated API boundary

The existing authentication runtime remains the single owner of the in-memory
access token and authenticated Axios client. It exposes the shared client to
feature API adapters without exposing the token value. Workspace pages call a
typed workspace API adapter rather than Axios directly.

An authenticated API response with HTTP 401 clears local authentication through
the existing auth boundary. HTTP 403 does not clear the session; the active page
shows a permission message and may refresh workspace data when a concurrent role
change could have invalidated the visible controls.

Access tokens and invitation tokens are never written to local storage or
session storage.

### Workspace state

A `WorkspaceProvider` inside the protected application boundary owns:

- the accessible workspace collection;
- its loading, empty, and error states;
- the last selected workspace ID for the current in-memory session only; and
- operations to add a newly created workspace and refresh the collection.

The selected workspace is derived from the protected route's `workspaceId`
parameter. The provider never treats an arbitrary route ID as accessible. A
workspace route resolves only when the ID exists in the latest accessible
collection.

Visiting `/app` selects the last valid in-memory workspace when available,
otherwise the Personal Workspace, otherwise the first accessible Team
Workspace. A defensive empty state remains available even though normal account
provisioning guarantees a Personal Workspace.

### Routing

Protected application routes use a nested shell and `Outlet`:

- `/app` selects an accessible workspace;
- `/app/workspaces/new` creates a Team Workspace;
- `/app/workspaces/:workspaceId` shows the workspace landing state;
- `/app/workspaces/:workspaceId/members` manages members and invitations; and
- `/app/account/sessions` renders the existing session management feature.

`/accept-invitation?token=...` is authenticated but rendered outside the
workspace shell because the accepting user may not yet have workspace access.
The existing login return path preserves the link through authentication. The
acceptance page keeps the token only in the URL and request memory. After a
successful request it replaces the current history entry with `/app`, refreshes
the workspace collection, and does not retain the token.

## Components

### Authenticated app shell

The production app shell contains the TempoDesk identity, workspace switcher,
workspace navigation, account navigation, and an `Outlet`. It replaces the
temporary development shell only for protected routes; public authentication
and landing routes retain their current layouts.

The account navigation links to device sessions. A new sign-out API or refresh
cookie revocation flow is outside this ticket.

### Workspace switcher

The switcher uses a styled native `select` because native keyboard navigation,
focus, and screen-reader behavior satisfy the ticket without introducing a new
interaction primitive. Personal and Team Workspaces are visually distinguished
with grouped options or adjacent labels. A separate action links to Team
Workspace creation.

Loading, empty, and load-failure states remain visible in the shell. Selecting
an option navigates to the matching route and records the ID only in React
memory. The switcher never persists workspace selection or permissions.

### Workspace creation

The creation page uses the existing form and feedback components. It validates
the required name locally for immediate feedback while relying on backend
validation as authoritative. A successful response is inserted into the
provider collection and becomes the active route. The returned `owner` role is
shown in the shell and workspace page.

### Members and invitations

The members page owns member and invitation loading and mutation state. It
loads both collections in parallel after the selected workspace resolves. The
page renders separate active-member and pending-invitation sections.

The page supports:

- inviting Admin, Member, or Viewer roles;
- cancelling a pending invitation;
- changing a non-Owner member role;
- removing an eligible member;
- leaving a Team Workspace; and
- transferring ownership to an active non-Owner member.

Removal, leaving, role escalation to Admin, and ownership transfer require an
explicit confirmation. Controls stay disabled while their mutation is pending.
Successful mutations update the affected collection from the response when
available or remove the affected entry for no-content responses. Ownership
transfer and leaving also refresh the workspace collection before navigating.

## Permission Presentation

The workspace response's effective role is the frontend permission input. A
small feature-local policy mirrors the fixed SCRUM-34 role matrix:

- every active role may view members;
- Owner and Admin may invite, change, or remove Member and Viewer roles;
- only Owner may manage Admin roles or transfer ownership; and
- Admin, Member, and Viewer may leave, while Owner must transfer ownership
  first.

Personal Workspace lifecycle mutations remain unavailable. Permission helpers
control visibility or disabled states only. Every mutation still handles HTTP
403 because role information can become stale and frontend checks are never an
authorization boundary.

## Feedback and Failure States

Pages distinguish initial loading, empty data, validation failure, conflict,
forbidden, unauthorized, and network failure. Existing `Alert`, `Spinner`, and
form components announce messages through `role="alert"` or `role="status"`.
Mutation failures leave the last confirmed server state visible and provide a
retry path where retry is safe.

HTTP 409 uses action-specific conflict guidance without assuming which
concurrent change occurred. HTTP 404 on a workspace-scoped request returns the
user to `/app` after refreshing accessible workspaces so inaccessible resource
existence is not exposed.

## Styling and Code Conventions

The shell follows Twenty's compact hierarchy and workspace-navigation cues but
does not copy `twenty-front` source. TempoDesk uses the existing teal semantic
tokens, global SCSS, `td-`-prefixed BEM blocks, Tabler icons, and shared UI
components. New code follows `.cursor/rules/frontend-clean-code.mdc` and avoids
new dependencies or speculative abstractions.

## Verification

Automated frontend tests are intentionally excluded. Final verification runs:

```bash
npm --prefix client/web run format:check
npm --prefix client/web run lint
npm --prefix client/web run build
```

Manual verification covers desktop and narrow layouts, light and dark themes,
keyboard-only workspace switching and actions, focus visibility, route refresh,
login return to an invitation link, confirmation flows, and loading, empty,
403, conflict, and network feedback.
