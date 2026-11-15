import { useEffect, useState } from 'react'
import type { AuthenticationSession } from '../../api/auth-api'
import { Alert, Button, Spinner } from '../../ui'
import { useAuth } from '../auth/state/AuthContext'
import { describeDevice, formatSessionDate } from './session-display'
import './SessionManagementPage.scss'

export function SessionManagementPage() {
  const { api, logoutLocal } = useAuth()
  const [sessions, setSessions] = useState<AuthenticationSession[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [pendingId, setPendingId] = useState<string | null>(null)
  const [revokingAll, setRevokingAll] = useState(false)

  useEffect(() => {
    let active = true
    void api
      .listSessions()
      .then((result) => {
        if (active) setSessions(result)
      })
      .catch(() => {
        if (active)
          setLoadError('Unable to load your sessions. Please try again.')
      })
    return () => {
      active = false
    }
  }, [api])

  async function retryLoad() {
    setLoadError(null)
    try {
      setSessions(await api.listSessions())
    } catch {
      setLoadError('Unable to load your sessions. Please try again.')
    }
  }

  async function revokeSession(session: AuthenticationSession) {
    const confirmed = window.confirm(
      session.isCurrent
        ? 'Revoke this session? You will be signed out on this device.'
        : 'Revoke this session? That device will no longer be able to refresh its login.',
    )
    if (!confirmed) return

    setPendingId(session.id)
    setActionError(null)
    setSuccess(null)
    try {
      await api.revokeSession(session.id)
      if (session.isCurrent) {
        logoutLocal()
        return
      }
      setSessions(
        (current) =>
          current?.map((item) =>
            item.id === session.id ? { ...item, status: 'revoked' } : item,
          ) ?? null,
      )
      setSuccess('Session revoked successfully.')
      try {
        setSessions(await api.listSessions())
      } catch {
        setActionError(
          'Session revoked, but the latest session list could not be loaded.',
        )
      }
    } catch {
      setActionError('Unable to revoke the session. Please try again.')
    } finally {
      setPendingId(null)
    }
  }

  async function revokeAllSessions() {
    const confirmed = window.confirm(
      'Revoke all sessions? You will be signed out on every device, including this one.',
    )
    if (!confirmed) return

    setRevokingAll(true)
    setActionError(null)
    setSuccess(null)
    try {
      await api.revokeAllSessions()
      logoutLocal()
    } catch {
      setActionError('Unable to revoke all sessions. Please try again.')
      setRevokingAll(false)
    }
  }

  const mutationPending = pendingId !== null || revokingAll
  const hasActiveSessions = sessions?.some(
    (session) => session.status === 'active',
  )

  return (
    <section className="td-sessions" aria-labelledby="sessions-heading">
      <header className="td-sessions__header">
        <div>
          <p className="td-sessions__eyebrow">Account security</p>
          <h1 id="sessions-heading">Manage sessions</h1>
          <p className="td-sessions__intro">
            Review the devices signed in to your account and revoke access you
            no longer recognize or use.
          </p>
        </div>
        <Button
          variant="danger"
          onClick={() => void revokeAllSessions()}
          loading={revokingAll}
          disabled={!hasActiveSessions || mutationPending}
        >
          Revoke all sessions
        </Button>
      </header>

      <div className="td-sessions__feedback">
        {loadError && (
          <Alert variant="error" role="alert" title="Sessions unavailable">
            <p>{loadError}</p>
            <Button
              variant="secondary"
              size="small"
              onClick={() => void retryLoad()}
            >
              Try again
            </Button>
          </Alert>
        )}
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

      {sessions === null && !loadError && (
        <div className="td-sessions__loading" role="status">
          <Spinner /> Loading sessions…
        </div>
      )}

      {sessions?.length === 0 && (
        <div className="td-sessions__empty">
          <h2>No sessions found</h2>
          <p>There are no login sessions available for this account.</p>
        </div>
      )}

      {sessions && sessions.length > 0 && (
        <ul className="td-sessions__list" aria-label="Login sessions">
          {sessions.map((session) => {
            const device = describeDevice(session.userAgent)
            return (
              <li className="td-sessions__item" key={session.id}>
                <article>
                  <div className="td-sessions__item-header">
                    <div>
                      <div className="td-sessions__title-row">
                        <h2>{device}</h2>
                        {session.isCurrent && (
                          <span className="td-sessions__badge td-sessions__badge--current">
                            Current session
                          </span>
                        )}
                        <span
                          className={`td-sessions__badge td-sessions__badge--${session.status}`}
                        >
                          {session.status}
                        </span>
                      </div>
                      {session.ipAddress && (
                        <p className="td-sessions__ip">
                          IP address {session.ipAddress}
                        </p>
                      )}
                    </div>
                    {session.status === 'active' && (
                      <Button
                        variant="danger"
                        size="small"
                        onClick={() => void revokeSession(session)}
                        loading={pendingId === session.id}
                        disabled={mutationPending}
                        aria-label={`Revoke ${device}${session.isCurrent ? ', current session' : ''}`}
                      >
                        Revoke
                      </Button>
                    )}
                  </div>
                  <dl className="td-sessions__metadata">
                    <div>
                      <dt>Created</dt>
                      <dd>
                        <time dateTime={session.createdAt}>
                          {formatSessionDate(session.createdAt)}
                        </time>
                      </dd>
                    </div>
                    <div>
                      <dt>Last active</dt>
                      <dd>
                        <time dateTime={session.lastUsedAt}>
                          {formatSessionDate(session.lastUsedAt)}
                        </time>
                      </dd>
                    </div>
                    <div>
                      <dt>Expires</dt>
                      <dd>
                        <time dateTime={session.expiresAt}>
                          {formatSessionDate(session.expiresAt)}
                        </time>
                      </dd>
                    </div>
                  </dl>
                </article>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}
