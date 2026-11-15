import type { PropsWithChildren } from 'react'
import { Navigate, useLocation } from 'react-router'
import { useAuth } from '../features/auth/state/AuthContext'
import { Spinner } from '../ui'

export function ProtectedRoute({ children }: PropsWithChildren) {
  const { status } = useAuth()
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
  return children
}
