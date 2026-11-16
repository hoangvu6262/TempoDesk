import { useState, type FormEvent } from 'react'
import type { ManageableWorkspaceRole } from '../../../api/workspace-api'
import { Button, FormField, Input } from '../../../ui'
import './WorkspaceManagement.scss'

const ROLE_LABELS = {
  admin: 'Admin',
  member: 'Member',
  viewer: 'Viewer',
} satisfies Record<ManageableWorkspaceRole, string>

export type InviteMemberFormProps = {
  allowedRoles: readonly ManageableWorkspaceRole[]
  emailError?: string
  loading: boolean
  onSubmit(email: string, role: ManageableWorkspaceRole): Promise<boolean>
}

export function InviteMemberForm({
  allowedRoles,
  emailError,
  loading,
  onSubmit,
}: InviteMemberFormProps) {
  const [email, setEmail] = useState('')
  const [selectedRole, setSelectedRole] =
    useState<ManageableWorkspaceRole>('member')
  const role = allowedRoles.includes(selectedRole)
    ? selectedRole
    : allowedRoles[0]

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!role) return
    if (await onSubmit(email.trim(), role)) setEmail('')
  }

  return (
    <form className="td-workspace-management__invite" onSubmit={submit}>
      <FormField label="Email" error={emailError} required>
        {(props) => (
          <Input
            {...props}
            type="email"
            autoComplete="email"
            disabled={loading}
            maxLength={256}
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        )}
      </FormField>
      <label className="td-workspace-management__role-field">
        <span>Role</span>
        <select
          disabled={loading}
          value={role}
          onChange={(event) =>
            setSelectedRole(event.target.value as ManageableWorkspaceRole)
          }
        >
          {allowedRoles.map((allowedRole) => (
            <option key={allowedRole} value={allowedRole}>
              {ROLE_LABELS[allowedRole]}
            </option>
          ))}
        </select>
      </label>
      <Button type="submit" loading={loading}>
        Send invitation
      </Button>
    </form>
  )
}
