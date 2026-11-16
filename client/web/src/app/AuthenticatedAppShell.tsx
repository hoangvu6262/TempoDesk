import { Link, NavLink, Outlet, useMatch, useNavigate } from 'react-router'
import { WorkspaceSwitcher } from '../features/workspaces/components/WorkspaceSwitcher'
import { useWorkspaces } from '../features/workspaces/state/WorkspaceContext'
import './AuthenticatedAppShell.scss'

export function AuthenticatedAppShell() {
  const navigate = useNavigate()
  const workspaceMatch = useMatch('/app/workspaces/:workspaceId/*')
  const { state, lastSelectedWorkspaceId, refreshWorkspaces, selectWorkspace } =
    useWorkspaces()
  const routeWorkspaceId = workspaceMatch?.params.workspaceId
  const selectedWorkspaceId =
    routeWorkspaceId ??
    (state.workspaces.some(
      (workspace) => workspace.id === lastSelectedWorkspaceId,
    )
      ? (lastSelectedWorkspaceId ?? '')
      : '')
  const selectedWorkspace = state.workspaces.find(
    (workspace) => workspace.id === selectedWorkspaceId,
  )

  function handleWorkspaceSelect(workspaceId: string) {
    selectWorkspace(workspaceId)
    navigate(`/app/workspaces/${encodeURIComponent(workspaceId)}`)
  }

  return (
    <div className="td-workspace-shell">
      <a className="td-skip-link" href="#main">
        Skip to content
      </a>
      <aside className="td-workspace-shell__sidebar">
        <Link className="td-brand" to="/app" aria-label="TempoDesk app home">
          <span className="td-brand__mark" aria-hidden="true">
            T
          </span>
          TempoDesk
        </Link>

        <WorkspaceSwitcher
          workspaces={state.workspaces}
          selectedWorkspaceId={selectedWorkspaceId}
          loading={state.status === 'loading'}
          error={state.status === 'error' ? state.message : undefined}
          onSelect={handleWorkspaceSelect}
          onRetry={() => void refreshWorkspaces().catch(() => undefined)}
        />

        <nav className="td-workspace-shell__nav" aria-label="Workspace">
          {selectedWorkspace && (
            <NavLink
              to={`/app/workspaces/${encodeURIComponent(selectedWorkspace.id)}`}
              end
            >
              Overview
            </NavLink>
          )}
          <NavLink to="/app/workspaces/new">Create Team Workspace</NavLink>
        </nav>

        <nav
          className="td-workspace-shell__nav td-workspace-shell__nav--account"
          aria-label="Account"
        >
          <span className="td-workspace-shell__nav-label">Account</span>
          <NavLink to="/app/account/sessions">Device sessions</NavLink>
        </nav>
      </aside>

      <div className="td-workspace-shell__body">
        <header className="td-workspace-shell__header">
          <div>
            <strong>{selectedWorkspace?.name ?? 'TempoDesk'}</strong>
            {selectedWorkspace && (
              <span>
                {selectedWorkspace.type === 'personal' ? 'Personal' : 'Team'} ·{' '}
                {selectedWorkspace.role}
              </span>
            )}
          </div>
        </header>
        <main className="td-workspace-shell__main" id="main">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
