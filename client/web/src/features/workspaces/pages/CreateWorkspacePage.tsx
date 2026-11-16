import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { ApiError } from '../../../lib/api'
import { Alert, Button, FormField, Input } from '../../../ui'
import { useWorkspaces } from '../state/WorkspaceContext'
import { workspaceFieldError, workspaceRequestError } from '../workspace-errors'
import './WorkspacePages.scss'

const WORKSPACE_NAME_MAX_LENGTH = 200

export function CreateWorkspacePage() {
  const navigate = useNavigate()
  const { api, addWorkspace } = useWorkspaces()
  const [name, setName] = useState('')
  const [nameError, setNameError] = useState<string>()
  const [requestError, setRequestError] = useState<string>()
  const [submitting, setSubmitting] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const normalizedName = name.trim()
    const nextNameError = !normalizedName
      ? 'Workspace name is required.'
      : normalizedName.length > WORKSPACE_NAME_MAX_LENGTH
        ? `Workspace name must not exceed ${WORKSPACE_NAME_MAX_LENGTH} characters.`
        : undefined
    setNameError(nextNameError)
    if (nextNameError) return

    setSubmitting(true)
    setRequestError(undefined)
    try {
      const workspace = await api.createWorkspace(normalizedName)
      addWorkspace(workspace)
      navigate(`/app/workspaces/${encodeURIComponent(workspace.id)}`, {
        replace: true,
      })
    } catch (error) {
      setNameError(workspaceFieldError(error, 'name'))
      setRequestError(
        error instanceof ApiError && error.status === 409
          ? 'The workspace could not be created because its state changed. Please try again.'
          : workspaceRequestError(
              error,
              'Unable to create the workspace. Please try again.',
            ),
      )
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section className="td-workspace-page" aria-labelledby="create-heading">
      <p className="td-workspace-page__eyebrow">Team Workspace</p>
      <h1 id="create-heading">Create a workspace</h1>
      <p className="td-workspace-page__intro">
        Create a shared space for your team. You will become its Owner.
      </p>

      <form className="td-workspace-page__form" onSubmit={submit}>
        {requestError && (
          <Alert variant="error" role="alert">
            {requestError}
          </Alert>
        )}
        <FormField label="Workspace name" error={nameError} required>
          {(props) => (
            <Input
              {...props}
              autoFocus
              disabled={submitting}
              maxLength={WORKSPACE_NAME_MAX_LENGTH + 1}
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
          )}
        </FormField>
        <div className="td-workspace-page__actions">
          <Button type="submit" loading={submitting}>
            Create workspace
          </Button>
          <Link to="/app">Cancel</Link>
        </div>
      </form>
    </section>
  )
}
