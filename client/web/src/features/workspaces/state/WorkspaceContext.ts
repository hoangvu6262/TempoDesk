import { createContext, useContext } from 'react'
import type { Workspace, WorkspaceApi } from '../../../api/workspace-api'

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

export const WorkspaceContext = createContext<WorkspaceContextValue | null>(
  null,
)

export function useWorkspaces() {
  const value = useContext(WorkspaceContext)
  if (!value)
    throw new Error('useWorkspaces must be used within WorkspaceProvider')
  return value
}
