import type {
  ManageableWorkspaceRole,
  WorkspaceMember,
  WorkspaceRole,
} from '../../../api/workspace-api'
import { Button } from '../../../ui'
import { formatWorkspaceDate } from '../workspace-display'
import {
  canChangeRole,
  canRemoveMember,
  canTransferOwnership,
} from '../workspace-permissions'
import './WorkspaceManagement.scss'

const OWNER_MANAGEABLE_ROLES = ['admin', 'member', 'viewer'] as const
const ADMIN_MANAGEABLE_ROLES = ['member', 'viewer'] as const

const ROLE_LABELS = {
  admin: 'Admin',
  member: 'Member',
  owner: 'Owner',
  viewer: 'Viewer',
} satisfies Record<WorkspaceRole, string>

export type PendingMemberAction = {
  kind: 'role' | 'remove' | 'transfer'
  memberId: string
}

export type MemberListProps = {
  members: WorkspaceMember[]
  actorRole: WorkspaceRole
  pendingAction: PendingMemberAction | null
  onChangeRole(member: WorkspaceMember, role: ManageableWorkspaceRole): void
  onRemove(member: WorkspaceMember): void
  onTransferOwnership(member: WorkspaceMember): void
}

export function MemberList({
  members,
  actorRole,
  pendingAction,
  onChangeRole,
  onRemove,
  onTransferOwnership,
}: MemberListProps) {
  function changeRole(member: WorkspaceMember, role: ManageableWorkspaceRole) {
    if (
      role === 'admin' &&
      member.role !== 'admin' &&
      !window.confirm(`Make ${member.email} an Admin?`)
    ) {
      return
    }
    onChangeRole(member, role)
  }

  function remove(member: WorkspaceMember) {
    if (!window.confirm(`Remove ${member.email} from this workspace?`)) return
    onRemove(member)
  }

  function transferOwnership(member: WorkspaceMember) {
    if (
      !window.confirm(
        `Transfer ownership to ${member.email}? You will become an Admin.`,
      )
    ) {
      return
    }
    onTransferOwnership(member)
  }

  return (
    <ul className="td-workspace-management__list" aria-label="Members">
      {members.map((member) => {
        const allowedRoles: readonly ManageableWorkspaceRole[] =
          actorRole === 'owner'
            ? OWNER_MANAGEABLE_ROLES
            : actorRole === 'admin' &&
                (member.role === 'member' || member.role === 'viewer')
              ? ADMIN_MANAGEABLE_ROLES
              : []
        const canEditRole = allowedRoles.some(
          (role) =>
            role !== member.role && canChangeRole(actorRole, member.role, role),
        )
        const canRemove = canRemoveMember(actorRole, member.role)
        const canTransfer =
          member.role !== 'owner' && canTransferOwnership(actorRole)
        const actionPending = pendingAction?.memberId === member.id
        const controlsDisabled = pendingAction !== null

        return (
          <li key={member.id}>
            <div className="td-workspace-management__identity">
              <strong>{member.email}</strong>
              <span>
                {ROLE_LABELS[member.role]} · Joined{' '}
                <time dateTime={member.joinedAt}>
                  {formatWorkspaceDate(member.joinedAt)}
                </time>
              </span>
            </div>

            {(canEditRole || canRemove || canTransfer) && (
              <div className="td-workspace-management__member-actions">
                {canEditRole && member.role !== 'owner' && (
                  <label className="td-workspace-management__member-role">
                    <span className="td-visually-hidden">
                      Role for {member.email}
                    </span>
                    <select
                      aria-label={`Role for ${member.email}`}
                      disabled={controlsDisabled}
                      value={member.role}
                      onChange={(event) =>
                        changeRole(
                          member,
                          event.target.value as ManageableWorkspaceRole,
                        )
                      }
                    >
                      {allowedRoles.map((role) => (
                        <option key={role} value={role}>
                          {ROLE_LABELS[role]}
                        </option>
                      ))}
                    </select>
                  </label>
                )}
                {canTransfer && (
                  <Button
                    variant="secondary"
                    size="small"
                    loading={
                      actionPending && pendingAction?.kind === 'transfer'
                    }
                    disabled={controlsDisabled}
                    onClick={() => transferOwnership(member)}
                  >
                    Transfer ownership
                  </Button>
                )}
                {canRemove && (
                  <Button
                    variant="danger"
                    size="small"
                    loading={actionPending && pendingAction?.kind === 'remove'}
                    disabled={controlsDisabled}
                    onClick={() => remove(member)}
                  >
                    Remove
                  </Button>
                )}
              </div>
            )}
          </li>
        )
      })}
    </ul>
  )
}
