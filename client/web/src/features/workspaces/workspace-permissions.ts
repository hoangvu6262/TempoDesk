import type {
  ManageableWorkspaceRole,
  WorkspaceRole,
} from '../../api/workspace-api'

const MEMBER_ROLES: ReadonlySet<WorkspaceRole> = new Set(['member', 'viewer'])

export function canInvite(
  actorRole: WorkspaceRole,
  invitedRole: ManageableWorkspaceRole,
) {
  return (
    actorRole === 'owner' ||
    (actorRole === 'admin' && MEMBER_ROLES.has(invitedRole))
  )
}

export function canChangeRole(
  actorRole: WorkspaceRole,
  currentRole: WorkspaceRole,
  nextRole: ManageableWorkspaceRole,
) {
  if (currentRole === 'owner') return false
  if (actorRole === 'owner') return true
  return (
    actorRole === 'admin' &&
    MEMBER_ROLES.has(currentRole) &&
    MEMBER_ROLES.has(nextRole)
  )
}

export function canRemoveMember(
  actorRole: WorkspaceRole,
  targetRole: WorkspaceRole,
) {
  if (targetRole === 'owner') return false
  return (
    actorRole === 'owner' ||
    (actorRole === 'admin' && MEMBER_ROLES.has(targetRole))
  )
}

export function canTransferOwnership(actorRole: WorkspaceRole) {
  return actorRole === 'owner'
}

export function canLeaveWorkspace(role: WorkspaceRole) {
  return role !== 'owner'
}
