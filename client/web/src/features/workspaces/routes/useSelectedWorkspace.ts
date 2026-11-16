import { useOutletContext } from 'react-router'
import type { Workspace } from '../../../api/workspace-api'

export type WorkspaceRouteContext = { workspace: Workspace }

export function useSelectedWorkspace() {
  return useOutletContext<WorkspaceRouteContext>().workspace
}
