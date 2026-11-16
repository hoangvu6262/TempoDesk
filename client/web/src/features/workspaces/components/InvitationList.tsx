import type {
  WorkspaceInvitation,
  WorkspaceRole,
} from '../../../api/workspace-api'
import { Button } from '../../../ui'
import { formatWorkspaceDate } from '../workspace-display'
import { canInvite } from '../workspace-permissions'
import './WorkspaceManagement.scss'

export type InvitationListProps = {
  invitations: WorkspaceInvitation[]
  actorRole: WorkspaceRole
  pendingId: string | null
  onCancel(invitationId: string): void
}

export function InvitationList({
  invitations,
  actorRole,
  pendingId,
  onCancel,
}: InvitationListProps) {
  if (invitations.length === 0) {
    return (
      <p className="td-workspace-management__empty">No pending invitations.</p>
    )
  }

  function confirmCancellation(invitation: WorkspaceInvitation) {
    if (!window.confirm(`Cancel the invitation for ${invitation.email}?`))
      return
    onCancel(invitation.id)
  }

  return (
    <ul
      className="td-workspace-management__list"
      aria-label="Pending invitations"
    >
      {invitations.map((invitation) => (
        <li key={invitation.id}>
          <div className="td-workspace-management__identity">
            <strong>{invitation.email}</strong>
            <span>
              {invitation.role} · Expires{' '}
              <time dateTime={invitation.expiresAt}>
                {formatWorkspaceDate(invitation.expiresAt)}
              </time>
            </span>
          </div>
          {canInvite(actorRole, invitation.role) && (
            <Button
              variant="danger"
              size="small"
              loading={pendingId === invitation.id}
              disabled={pendingId !== null}
              onClick={() => confirmCancellation(invitation)}
            >
              Cancel
            </Button>
          )}
        </li>
      ))}
    </ul>
  )
}
