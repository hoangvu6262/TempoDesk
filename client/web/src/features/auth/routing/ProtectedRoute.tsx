import type { PropsWithChildren } from 'react'
import { Navigate, useLocation } from 'react-router'
import { Spinner } from '../../../ui'
import { useAuth } from '../state/AuthContext'

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
