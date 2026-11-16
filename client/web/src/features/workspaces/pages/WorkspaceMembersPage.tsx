import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import type {
  ManageableWorkspaceRole,
  WorkspaceInvitation,
  WorkspaceMember,
} from '../../../api/workspace-api'
import { ApiError } from '../../../lib/api'
import { Alert, Button, Spinner } from '../../../ui'
import { InvitationList } from '../components/InvitationList'
import { InviteMemberForm } from '../components/InviteMemberForm'
import { formatWorkspaceDate } from '../workspace-display'
import { useSelectedWorkspace } from '../routes/useSelectedWorkspace'
import { useWorkspaces } from '../state/WorkspaceContext'
import { workspaceFieldError, workspaceRequestError } from '../workspace-errors'
import '../components/WorkspaceManagement.scss'

const OWNER_INVITE_ROLES = ['admin', 'member', 'viewer'] as const
const ADMIN_INVITE_ROLES = ['member', 'viewer'] as const

type ManagementData = {
  members: WorkspaceMember[]
  invitations: WorkspaceInvitation[]
}

export function WorkspaceMembersPage() {
  const navigate = useNavigate()
  const workspace = useSelectedWorkspace()
  const { api, refreshWorkspaces } = useWorkspaces()
  const [data, setData] = useState<ManagementData | null>(null)
  const [loadError, setLoadError] = useState<string>()
  const [actionError, setActionError] = useState<string>()
  const [success, setSuccess] = useState<string>()
  const [emailError, setEmailError] = useState<string>()
  const [inviting, setInviting] = useState(false)
  const [pendingInvitationId, setPendingInvitationId] = useState<string | null>(
    null,
  )
  const [loadVersion, setLoadVersion] = useState(0)

  useEffect(() => {
    let active = true
    void Promise.all([
      api.listMembers(workspace.id),
      api.listInvitations(workspace.id),
    ])
      .then(([members, invitations]) => {
        if (active) setData({ members, invitations })
      })
      .catch((error: unknown) => {
        if (!active) return
        if (error instanceof ApiError && error.status === 404) {
          void refreshWorkspaces().finally(() =>
            navigate('/app', { replace: true }),
          )
          return
        }
        setLoadError(
          error instanceof ApiError && error.status === 403
            ? 'You do not have permission to view workspace members.'
            : 'Unable to load members and invitations. Please try again.',
        )
      })
    return () => {
      active = false
    }
  }, [api, loadVersion, navigate, refreshWorkspaces, workspace.id])

  const allowedInviteRoles: readonly ManageableWorkspaceRole[] =
    workspace.type !== 'team'
      ? []
      : workspace.role === 'owner'
        ? OWNER_INVITE_ROLES
        : workspace.role === 'admin'
          ? ADMIN_INVITE_ROLES
          : []

  async function mutationError(
    error: unknown,
    conflictMessage: string,
    fallback: string,
  ) {
    if (error instanceof ApiError && error.status === 404) {
      await refreshWorkspaces().catch(() => undefined)
      navigate('/app', { replace: true })
      return undefined
    }
    if (error instanceof ApiError && error.status === 403) {
      void refreshWorkspaces().catch(() => undefined)
      return 'You no longer have permission to perform this action.'
    }
    if (error instanceof ApiError && error.status === 409) {
      return conflictMessage
    }
    return workspaceRequestError(error, fallback)
  }

  async function invite(email: string, role: ManageableWorkspaceRole) {
    setInviting(true)
    setEmailError(undefined)
    setActionError(undefined)
    setSuccess(undefined)
    try {
      const invitation = await api.invite(workspace.id, email, role)
      setData((current) =>
        current
          ? {
              ...current,
              invitations: [...current.invitations, invitation],
            }
          : current,
      )
      setSuccess(`Invitation sent to ${invitation.email}.`)
      return true
    } catch (error) {
      setEmailError(workspaceFieldError(error, 'email'))
      setActionError(
        await mutationError(
          error,
          'This person is already a member or has an active invitation.',
          'Unable to send the invitation. Please try again.',
        ),
      )
      return false
    } finally {
      setInviting(false)
    }
  }

  async function cancelInvitation(invitationId: string) {
    setPendingInvitationId(invitationId)
    setActionError(undefined)
    setSuccess(undefined)
    try {
      await api.cancelInvitation(workspace.id, invitationId)
      setData((current) =>
        current
          ? {
              ...current,
              invitations: current.invitations.filter(
                (invitation) => invitation.id !== invitationId,
              ),
            }
          : current,
      )
      setSuccess('Invitation cancelled.')
    } catch (error) {
      setActionError(
        await mutationError(
          error,
          'The invitation is no longer active.',
          'Unable to cancel the invitation. Please try again.',
        ),
      )
    } finally {
      setPendingInvitationId(null)
    }
  }

  if (!data && !loadError) {
    return (
      <div className="td-workspace-page__state" role="status">
        <Spinner /> Loading members…
      </div>
    )
  }

  if (!data) {
    return (
      <Alert variant="error" role="alert" title="Members unavailable">
        <p>{loadError}</p>
        <Button
          variant="secondary"
          size="small"
          onClick={() => {
            setLoadError(undefined)
            setLoadVersion((current) => current + 1)
          }}
        >
          Try again
        </Button>
      </Alert>
    )
  }

  return (
    <section
      className="td-workspace-management"
      aria-labelledby="members-heading"
    >
      <header className="td-workspace-management__header">
        <h1 id="members-heading">Members</h1>
        <p>Manage access to {workspace.name}.</p>
      </header>

      <div className="td-workspace-management__feedback">
        {actionError && (
          <Alert variant="error" role="alert">
            {actionError}
          </Alert>
        )}
        {success && (
          <Alert variant="success" role="status">
            {success}
          </Alert>
        )}
      </div>

      {allowedInviteRoles.length > 0 && (
        <section className="td-workspace-management__section">
          <div className="td-workspace-management__section-header">
            <h2>Invite someone</h2>
            <p>Send an invitation to join this Team Workspace.</p>
          </div>
          <InviteMemberForm
            allowedRoles={allowedInviteRoles}
            emailError={emailError}
            loading={inviting}
            onSubmit={invite}
          />
        </section>
      )}

      <section className="td-workspace-management__section">
        <div className="td-workspace-management__section-header">
          <h2>Active members</h2>
        </div>
        <ul className="td-workspace-management__list" aria-label="Members">
          {data.members.map((member) => (
            <li key={member.id}>
              <div className="td-workspace-management__identity">
                <strong>{member.email}</strong>
                <span>
                  {member.role} · Joined{' '}
                  <time dateTime={member.joinedAt}>
                    {formatWorkspaceDate(member.joinedAt)}
                  </time>
                </span>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <section className="td-workspace-management__section">
        <div className="td-workspace-management__section-header">
          <h2>Pending invitations</h2>
        </div>
        <InvitationList
          invitations={data.invitations}
          actorRole={workspace.role}
          pendingId={pendingInvitationId}
          onCancel={(invitationId) => void cancelInvitation(invitationId)}
        />
      </section>
    </section>
  )
}
