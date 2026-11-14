import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { AuthenticationApi } from '../api/auth-api'
import {
  AuthContext,
  type AuthContextValue,
  type AuthStatus,
} from '../features/auth/state/AuthContext'
import { ProtectedRoute } from './ProtectedRoute'

const api = {} as AuthenticationApi

function LocationProbe() {
  const location = useLocation()
  return (
    <span data-testid="location">
      {location.pathname}
      {location.search}
      {location.hash}
    </span>
  )
}

function renderRoute(status: AuthStatus, entry: string) {
  const value: AuthContextValue = {
    status,
    api,
    login: vi.fn(),
    logoutLocal: vi.fn(),
  }
  render(
    <AuthContext value={value}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route
            path="/app"
            element={
              <ProtectedRoute>
                <span>Private content</span>
              </ProtectedRoute>
            }
          />
          <Route path="/login" element={<LocationProbe />} />
        </Routes>
      </MemoryRouter>
    </AuthContext>,
  )
}

describe('ProtectedRoute', () => {
  it('does not render private content while initializing', () => {
    renderRoute('initializing', '/app?view=mine#today')
    expect(screen.getByRole('status')).toHaveTextContent(
      'Checking your session',
    )
    expect(screen.queryByText('Private content')).not.toBeInTheDocument()
  })

  it('redirects anonymous users with the complete return path', async () => {
    renderRoute('anonymous', '/app?view=mine#today')
    expect(await screen.findByTestId('location')).toHaveTextContent(
      '/login?returnTo=%2Fapp%3Fview%3Dmine%23today',
    )
  })

  it('renders children for authenticated users', () => {
    renderRoute('authenticated', '/app')
    expect(screen.getByText('Private content')).toBeVisible()
  })
})
