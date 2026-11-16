import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Outlet } from 'react-router'
import { createWorkspaceApi, type Workspace } from '../../../api/workspace-api'
import { useApiRuntime } from '../../../lib/api'
import { WorkspaceContext, type WorkspaceLoadState } from './WorkspaceContext'

const LOAD_ERROR_MESSAGE = 'Unable to load your workspaces. Please try again.'

export function WorkspaceProvider() {
  const { client } = useApiRuntime()
  const api = useMemo(() => createWorkspaceApi(client), [client])
  const activeRef = useRef(true)
  const requestIdRef = useRef(0)
  const [state, setState] = useState<WorkspaceLoadState>({
    status: 'loading',
    workspaces: [],
  })
  const [lastSelectedWorkspaceId, setLastSelectedWorkspaceId] = useState<
    string | null
  >(null)

  const refreshWorkspaces = useCallback(async () => {
    const requestId = ++requestIdRef.current
    setState((current) =>
      current.workspaces.length
        ? current
        : { status: 'loading', workspaces: [] },
    )

    try {
      const workspaces = await api.listWorkspaces()
      if (activeRef.current && requestId === requestIdRef.current) {
        setState({ status: 'ready', workspaces })
      }
      return workspaces
    } catch (error) {
      if (activeRef.current && requestId === requestIdRef.current) {
        setState((current) => ({
          status: 'error',
          workspaces: current.workspaces,
          message: LOAD_ERROR_MESSAGE,
        }))
      }
      throw error
    }
  }, [api])

  useEffect(() => {
    activeRef.current = true
    const requestId = ++requestIdRef.current
    void api
      .listWorkspaces()
      .then((workspaces) => {
        if (activeRef.current && requestId === requestIdRef.current) {
          setState({ status: 'ready', workspaces })
        }
      })
      .catch(() => {
        if (activeRef.current && requestId === requestIdRef.current) {
          setState({
            status: 'error',
            workspaces: [],
            message: LOAD_ERROR_MESSAGE,
          })
        }
      })
    return () => {
      activeRef.current = false
      requestIdRef.current += 1
    }
  }, [api])

  const addWorkspace = useCallback((workspace: Workspace) => {
    setState((current) => ({
      status: 'ready',
      workspaces: current.workspaces.some((item) => item.id === workspace.id)
        ? current.workspaces
        : [...current.workspaces, workspace],
    }))
    setLastSelectedWorkspaceId(workspace.id)
  }, [])

  const selectWorkspace = useCallback(
    (workspaceId: string) => {
      if (state.workspaces.some((workspace) => workspace.id === workspaceId)) {
        setLastSelectedWorkspaceId(workspaceId)
      }
    },
    [state.workspaces],
  )

  const value = useMemo(
    () => ({
      state,
      lastSelectedWorkspaceId,
      api,
      refreshWorkspaces,
      addWorkspace,
      selectWorkspace,
    }),
    [
      addWorkspace,
      api,
      lastSelectedWorkspaceId,
      refreshWorkspaces,
      selectWorkspace,
      state,
    ],
  )

  return (
    <WorkspaceContext value={value}>
      <Outlet />
    </WorkspaceContext>
  )
}
