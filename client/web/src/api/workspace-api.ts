import type { AxiosInstance } from 'axios'

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

export type WorkspaceApi = {
  listWorkspaces(): Promise<Workspace[]>
  createWorkspace(name: string): Promise<Workspace>
  listMembers(workspaceId: string): Promise<WorkspaceMember[]>
  listInvitations(workspaceId: string): Promise<WorkspaceInvitation[]>
  invite(
    workspaceId: string,
    email: string,
    role: ManageableWorkspaceRole,
  ): Promise<WorkspaceInvitation>
  cancelInvitation(workspaceId: string, invitationId: string): Promise<void>
  acceptInvitation(token: string): Promise<WorkspaceMember>
  changeRole(
    workspaceId: string,
    memberId: string,
    role: ManageableWorkspaceRole,
  ): Promise<WorkspaceMember>
  removeMember(workspaceId: string, memberId: string): Promise<void>
  leave(workspaceId: string): Promise<void>
  transferOwnership(workspaceId: string, memberId: string): Promise<void>
}

const pathId = (value: string) => encodeURIComponent(value)

export function createWorkspaceApi(client: AxiosInstance): WorkspaceApi {
  return {
    listWorkspaces: async () =>
      (await client.get<Workspace[]>('/api/workspaces')).data,
    createWorkspace: async (name) =>
      (await client.post<Workspace>('/api/workspaces', { name })).data,
    listMembers: async (workspaceId) =>
      (
        await client.get<WorkspaceMember[]>(
          `/api/workspaces/${pathId(workspaceId)}/members`,
        )
      ).data,
    listInvitations: async (workspaceId) =>
      (
        await client.get<WorkspaceInvitation[]>(
          `/api/workspaces/${pathId(workspaceId)}/invitations`,
        )
      ).data,
    invite: async (workspaceId, email, role) =>
      (
        await client.post<WorkspaceInvitation>(
          `/api/workspaces/${pathId(workspaceId)}/invitations`,
          { email, role },
        )
      ).data,
    cancelInvitation: async (workspaceId, invitationId) => {
      await client.delete(
        `/api/workspaces/${pathId(workspaceId)}/invitations/${pathId(invitationId)}`,
      )
    },
    acceptInvitation: async (token) =>
      (
        await client.post<WorkspaceMember>(
          '/api/workspace-invitations/accept',
          { token },
        )
      ).data,
    changeRole: async (workspaceId, memberId, role) =>
      (
        await client.patch<WorkspaceMember>(
          `/api/workspaces/${pathId(workspaceId)}/members/${pathId(memberId)}`,
          { role },
        )
      ).data,
    removeMember: async (workspaceId, memberId) => {
      await client.delete(
        `/api/workspaces/${pathId(workspaceId)}/members/${pathId(memberId)}`,
      )
    },
    leave: async (workspaceId) => {
      await client.post(`/api/workspaces/${pathId(workspaceId)}/leave`)
    },
    transferOwnership: async (workspaceId, memberId) => {
      await client.post(
        `/api/workspaces/${pathId(workspaceId)}/transfer-ownership`,
        { memberId },
      )
    },
  }
}
