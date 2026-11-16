import type { PropsWithChildren } from 'react'
import { Navigate, useLocation } from 'react-router'
import { useAuth } from '../features/auth/state/AuthContext'
import { Alert, Button, Spinner } from '../ui'

export function ProtectedRoute({ children }: PropsWithChildren) {
  const { status, retrySession } = useAuth()
  const location = useLocation()

  if (status === 'initializing') {
    return (
      <div role="status">
        <Spinner /> Checking your session…
      </div>
    )
  }
  if (status === 'anonymous') {
    const returnTo = `${location.pathname}${location.search}${location.hash}`
    return (
      <Navigate
        replace
        to={`/login?returnTo=${encodeURIComponent(returnTo)}`}
      />
    )
  }
  if (status === 'unavailable') {
    return (
      <Alert variant="error" role="alert" title="Session unavailable">
        <p>Check your connection, then try restoring your session.</p>
        <Button
          variant="secondary"
          size="small"
          onClick={() => void retrySession()}
        >
          Try again
        </Button>
      </Alert>
    )
  }
  return children
}
