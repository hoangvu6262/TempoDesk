import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { AcceptedResponse, AuthenticationApi } from '../api/auth-api'
import { AuthContext } from '../state/AuthContext'
import { ForgotPasswordPage } from './ForgotPasswordPage'

function fakeApi(
  overrides: Partial<AuthenticationApi> = {},
): AuthenticationApi {
  return {
    register: vi.fn(),
    verifyEmail: vi.fn(),
    resendVerification: vi.fn(),
    login: vi.fn(),
    forgotPassword: vi.fn(),
    resetPassword: vi.fn(),
    refresh: vi.fn(),
    ...overrides,
  }
}

function renderPage(api: AuthenticationApi) {
  render(
    <AuthContext
      value={{
        status: 'anonymous',
        api,
        login: vi.fn(),
        logoutLocal: vi.fn(),
      }}
    >
      <MemoryRouter>
        <ForgotPasswordPage />
      </MemoryRouter>
    </AuthContext>,
  )
}

describe('ForgotPasswordPage', () => {
  it('validates email and displays the neutral API response', async () => {
    const user = userEvent.setup()
    const forgotPassword = vi.fn().mockResolvedValue({
      message: 'If the account exists, a password reset email will be sent.',
    })
    renderPage(fakeApi({ forgotPassword }))

    await user.click(screen.getByRole('button', { name: 'Send reset link' }))
    expect(screen.getByText('Email is required.')).toBeVisible()
    expect(forgotPassword).not.toHaveBeenCalled()

    await user.type(screen.getByLabelText(/^Email/), 'person@example.com')
    await user.click(screen.getByRole('button', { name: 'Send reset link' }))

    expect(forgotPassword).toHaveBeenCalledWith('person@example.com')
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'If the account exists, a password reset email will be sent.',
    )
  })

  it('prevents duplicate submission while pending', async () => {
    const user = userEvent.setup()
    const forgotPassword = vi.fn(() => new Promise<AcceptedResponse>(() => {}))
    renderPage(fakeApi({ forgotPassword }))
    await user.type(screen.getByLabelText(/^Email/), 'person@example.com')

    const submit = screen.getByRole('button', { name: 'Send reset link' })
    await user.click(submit)
    await user.click(submit)

    expect(forgotPassword).toHaveBeenCalledTimes(1)
    expect(submit).toBeDisabled()
  })
})
