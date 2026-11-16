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
import { MemberList, type PendingMemberAction } from '../components/MemberList'
import { useSelectedWorkspace } from '../routes/useSelectedWorkspace'
import { useWorkspaces } from '../state/WorkspaceContext'
import { workspaceFieldError, workspaceRequestError } from '../workspace-errors'
import { canLeaveWorkspace } from '../workspace-permissions'
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
  const [pendingMemberAction, setPendingMemberAction] =
    useState<PendingMemberAction | null>(null)
  const [leaving, setLeaving] = useState(false)
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

  async function changeRole(
    member: WorkspaceMember,
    role: ManageableWorkspaceRole,
  ) {
    if (member.role === role) return
    setPendingMemberAction({ kind: 'role', memberId: member.id })
    setActionError(undefined)
    setSuccess(undefined)
    try {
      const updatedMember = await api.changeRole(workspace.id, member.id, role)
      setData((current) =>
        current
          ? {
              ...current,
              members: current.members.map((currentMember) =>
                currentMember.id === updatedMember.id
                  ? updatedMember
                  : currentMember,
              ),
            }
          : current,
      )
      setSuccess(`${member.email} is now ${role}.`)
    } catch (error) {
      setActionError(
        await mutationError(
          error,
          'The member role changed before this request completed. Refresh and try again.',
          'Unable to change the member role. Please try again.',
        ),
      )
    } finally {
      setPendingMemberAction(null)
    }
  }

  async function removeMember(member: WorkspaceMember) {
    setPendingMemberAction({ kind: 'remove', memberId: member.id })
    setActionError(undefined)
    setSuccess(undefined)
    try {
      await api.removeMember(workspace.id, member.id)
      setData((current) =>
        current
          ? {
              ...current,
              members: current.members.filter(
                (currentMember) => currentMember.id !== member.id,
              ),
            }
          : current,
      )
      setSuccess(`${member.email} was removed from the workspace.`)
    } catch (error) {
      setActionError(
        await mutationError(
          error,
          'The member could not be removed because their workspace access changed.',
          'Unable to remove the member. Please try again.',
        ),
      )
    } finally {
      setPendingMemberAction(null)
    }
  }

  async function transferOwnership(member: WorkspaceMember) {
    setPendingMemberAction({ kind: 'transfer', memberId: member.id })
    setActionError(undefined)
    setSuccess(undefined)
    try {
      await api.transferOwnership(workspace.id, member.id)
      setSuccess(`Ownership transferred to ${member.email}.`)

      const [workspaceResult, managementResult] = await Promise.allSettled([
        refreshWorkspaces(),
        Promise.all([
          api.listMembers(workspace.id),
          api.listInvitations(workspace.id),
        ]),
      ])
      if (managementResult.status === 'fulfilled') {
        const [members, invitations] = managementResult.value
        setData({ members, invitations })
      }
      if (
        workspaceResult.status === 'rejected' ||
        managementResult.status === 'rejected'
      ) {
        setActionError(
          'Ownership was transferred, but the latest workspace details could not be loaded. Refresh to see the current access.',
        )
      }
    } catch (error) {
      setActionError(
        await mutationError(
          error,
          'Ownership could not be transferred because workspace membership changed.',
          'Unable to transfer ownership. Please try again.',
        ),
      )
    } finally {
      setPendingMemberAction(null)
    }
  }

  async function leaveWorkspace() {
    if (
      !window.confirm(
        `Leave ${workspace.name}? You will lose access to this workspace.`,
      )
    ) {
      return
    }

    setLeaving(true)
    setActionError(undefined)
    setSuccess(undefined)
    try {
      await api.leave(workspace.id)
      await refreshWorkspaces().catch(() => undefined)
      navigate('/app', { replace: true })
    } catch (error) {
      setActionError(
        await mutationError(
          error,
          'You cannot leave because your workspace access changed.',
          'Unable to leave the workspace. Please try again.',
        ),
      )
    } finally {
      setLeaving(false)
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
        <MemberList
          members={data.members}
          actorRole={workspace.role}
          pendingAction={pendingMemberAction}
          onChangeRole={(member, role) => void changeRole(member, role)}
          onRemove={(member) => void removeMember(member)}
          onTransferOwnership={(member) => void transferOwnership(member)}
        />
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

      {workspace.type === 'team' && canLeaveWorkspace(workspace.role) && (
        <section className="td-workspace-management__section td-workspace-management__section--danger">
          <div className="td-workspace-management__section-header">
            <h2>Leave workspace</h2>
            <p>You will need a new invitation to regain access.</p>
          </div>
          <Button
            variant="danger"
            loading={leaving}
            disabled={pendingMemberAction !== null}
            onClick={() => void leaveWorkspace()}
          >
            Leave workspace
          </Button>
        </section>
      )}
    </section>
  )
}
