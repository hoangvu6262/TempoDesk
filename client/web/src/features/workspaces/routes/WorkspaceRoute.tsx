import { useEffect } from 'react'
import { Navigate, Outlet, useParams } from 'react-router'
import { Alert, Button, Spinner } from '../../../ui'
import { useWorkspaces } from '../state/WorkspaceContext'
import type { WorkspaceRouteContext } from './useSelectedWorkspace'
import '../pages/WorkspacePages.scss'

export function WorkspaceRoute() {
  const { workspaceId } = useParams()
  const { state, refreshWorkspaces, selectWorkspace } = useWorkspaces()
  const workspace = state.workspaces.find((item) => item.id === workspaceId)

  useEffect(() => {
    if (workspace) selectWorkspace(workspace.id)
  }, [selectWorkspace, workspace])

  if (state.status === 'loading') {
    return (
      <div className="td-workspace-page__state" role="status">
        <Spinner /> Loading workspace…
      </div>
    )
  }

  if (!workspace && state.status === 'error') {
    return (
      <Alert variant="error" role="alert" title="Workspace unavailable">
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

  if (!workspace) return <Navigate replace to="/app" />

  return (
    <Outlet
      key={workspace.id}
      context={{ workspace } satisfies WorkspaceRouteContext}
    />
  )
}
