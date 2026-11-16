import type { Workspace } from '../../../api/workspace-api'
import { Alert, Button, Spinner } from '../../../ui'
import './WorkspaceSwitcher.scss'

export type WorkspaceSwitcherProps = {
  workspaces: Workspace[]
  selectedWorkspaceId: string
  loading: boolean
  error?: string
  disabled?: boolean
  onSelect(workspaceId: string): void
  onRetry(): void
}

export function WorkspaceSwitcher({
  workspaces,
  selectedWorkspaceId,
  loading,
  error,
  disabled = false,
  onSelect,
  onRetry,
}: WorkspaceSwitcherProps) {
  const personalWorkspaces = workspaces.filter(
    (workspace) => workspace.type === 'personal',
  )
  const teamWorkspaces = workspaces.filter(
    (workspace) => workspace.type === 'team',
  )

  return (
    <div className="td-workspace-switcher">
      <label
        className="td-workspace-switcher__label"
        htmlFor="workspace-switcher"
      >
        Workspace
      </label>
      <div className="td-workspace-switcher__control">
        <select
          id="workspace-switcher"
          value={selectedWorkspaceId}
          disabled={disabled || loading || workspaces.length === 0}
          onChange={(event) => onSelect(event.target.value)}
        >
          {!selectedWorkspaceId && <option value="">Choose a workspace</option>}
          {personalWorkspaces.length > 0 && (
            <optgroup label="Personal">
              {personalWorkspaces.map((workspace) => (
                <option key={workspace.id} value={workspace.id}>
                  {workspace.name}
                </option>
              ))}
            </optgroup>
          )}
          {teamWorkspaces.length > 0 && (
            <optgroup label="Team Workspaces">
              {teamWorkspaces.map((workspace) => (
                <option key={workspace.id} value={workspace.id}>
                  {workspace.name}
                </option>
              ))}
            </optgroup>
          )}
        </select>
        {loading && <Spinner size="small" label="Loading workspaces" />}
      </div>
      {error && (
        <Alert variant="error" role="alert" title="Workspaces unavailable">
          <p>{error}</p>
          <Button size="small" variant="secondary" onClick={onRetry}>
            Try again
          </Button>
        </Alert>
      )}
      {!loading && !error && workspaces.length === 0 && (
        <p className="td-workspace-switcher__empty" role="status">
          No workspaces available.
        </p>
      )}
    </div>
  )
}
