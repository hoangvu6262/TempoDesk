import { useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { createWorkspaceApi } from '../../../api/workspace-api'
import { ApiError, useApiRuntime } from '../../../lib/api'
import { Alert, Button } from '../../../ui'
import { workspaceRequestError } from '../workspace-errors'
import './WorkspacePages.scss'

const INVALID_INVITATION_MESSAGE =
  'This invitation is invalid or no longer active.'

export function AcceptInvitationPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const { client } = useApiRuntime()
  const api = useMemo(() => createWorkspaceApi(client), [client])
  const token = searchParams.get('token')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string>()

  async function acceptInvitation() {
    if (!token) return
    setSubmitting(true)
    setError(undefined)
    try {
      await api.acceptInvitation(token)
      navigate('/app', { replace: true })
    } catch (requestError) {
      if (requestError instanceof ApiError) {
        if (requestError.status === 400) {
          setError(INVALID_INVITATION_MESSAGE)
        } else if (requestError.status === 403) {
          setError('You do not have permission to accept this invitation.')
        } else if (requestError.status === 409) {
          setError('The invitation conflicts with the current workspace state.')
        } else {
          setError(
            workspaceRequestError(
              requestError,
              'Unable to accept the invitation. Please try again.',
            ),
          )
        }
      } else {
        setError('Unable to accept the invitation. Please try again.')
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <main className="td-invitation-page" id="main">
      <section
        className="td-workspace-page td-invitation-page__card"
        aria-labelledby="invitation-heading"
      >
        <p className="td-workspace-page__eyebrow">Workspace invitation</p>
        <h1 id="invitation-heading">Join this workspace</h1>
        <p className="td-workspace-page__intro">
          Accept the invitation to add this Team Workspace to your account.
        </p>
        {!token ? (
          <Alert variant="error" role="alert">
            {INVALID_INVITATION_MESSAGE}
          </Alert>
        ) : (
          <div className="td-invitation-page__actions">
            {error && (
              <Alert variant="error" role="alert">
                {error}
              </Alert>
            )}
            <Button
              loading={submitting}
              onClick={() => void acceptInvitation()}
            >
              Accept invitation
            </Button>
          </div>
        )}
      </section>
    </main>
  )
}
