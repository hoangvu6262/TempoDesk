import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { AuthenticationApi } from '../api/auth-api'
import { AuthContext } from '../state/AuthContext'
import { LoginPage } from './LoginPage'

const api = {} as AuthenticationApi

function LocationProbe() {
  const location = useLocation()
  return (
    <span data-testid="location">
      {location.pathname}
      {location.search}
    </span>
  )
}

function renderLogin(
  entry: string,
  login = vi.fn().mockResolvedValue(undefined),
) {
  render(
    <AuthContext
      value={{ status: 'anonymous', api, login, logoutLocal: vi.fn() }}
    >
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/app" element={<LocationProbe />} />
        </Routes>
      </MemoryRouter>
    </AuthContext>,
  )
  return login
}

describe('LoginPage', () => {
  it('logs in and returns to a safe requested route', async () => {
    const user = userEvent.setup()
    const login = renderLogin('/login?returnTo=%2Fapp%3Fview%3Dmine')

    await user.type(screen.getByLabelText(/^Email/), 'person@example.com')
    await user.type(screen.getByLabelText(/^Password/), 'password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(login).toHaveBeenCalledWith('person@example.com', 'password')
    expect(await screen.findByTestId('location')).toHaveTextContent(
      '/app?view=mine',
    )
  })

  it('falls back to /app for an unsafe return destination', async () => {
    const user = userEvent.setup()
    renderLogin('/login?returnTo=https%3A%2F%2Fevil.example')
    await user.type(screen.getByLabelText(/^Email/), 'person@example.com')
    await user.type(screen.getByLabelText(/^Password/), 'password')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByTestId('location')).toHaveTextContent('/app')
  })

  it('validates locally and uses a generic authentication error', async () => {
    const user = userEvent.setup()
    const login = vi.fn().mockRejectedValue(new Error('private'))
    renderLogin('/login', login)
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(screen.getByText('Email is required.')).toBeVisible()
    expect(login).not.toHaveBeenCalled()

    await user.type(screen.getByLabelText(/^Email/), 'person@example.com')
    await user.type(screen.getByLabelText(/^Password/), 'wrong')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Email or password is incorrect.',
    )
    expect(screen.queryByText('private')).not.toBeInTheDocument()
  })

  it('keeps Google unavailable without starting OAuth', () => {
    renderLogin('/login')
    expect(
      screen.getByRole('button', {
        name: 'Continue with Google — unavailable',
      }),
    ).toBeDisabled()
    expect(screen.getByText(/Chưa khả dụng/)).toBeVisible()
  })
})
