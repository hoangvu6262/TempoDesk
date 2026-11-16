import { Link, Navigate } from 'react-router'
import { Alert, Button, Spinner } from '../../../ui'
import { useWorkspaces } from '../state/WorkspaceContext'
import '../pages/WorkspacePages.scss'

export function WorkspaceEntryPage() {
  const { state, lastSelectedWorkspaceId, refreshWorkspaces } = useWorkspaces()

  if (state.status === 'loading') {
    return (
      <div className="td-workspace-page__state" role="status">
        <Spinner /> Loading workspaces…
      </div>
    )
  }

  const lastSelectedWorkspace = state.workspaces.find(
    (workspace) => workspace.id === lastSelectedWorkspaceId,
  )
  const personalWorkspace = state.workspaces.find(
    (workspace) => workspace.type === 'personal',
  )
  const destination =
    lastSelectedWorkspace ?? personalWorkspace ?? state.workspaces[0]

  if (destination) {
    return (
      <Navigate
        replace
        to={`/app/workspaces/${encodeURIComponent(destination.id)}`}
      />
    )
  }

  if (state.status === 'error') {
    return (
      <Alert variant="error" role="alert" title="Workspaces unavailable">
        <p>{state.message}</p>
        <Button
          variant="secondary"
          size="small"
          onClick={() => void refreshWorkspaces().catch(() => undefined)}
        >
          Try again
        </Button>
      </Alert>
    )
  }

  return (
    <section className="td-workspace-page__empty">
      <h1>No workspaces available</h1>
      <p>Create a Team Workspace to start using TempoDesk.</p>
      <Link to="/app/workspaces/new">Create Team Workspace</Link>
    </section>
  )
}
