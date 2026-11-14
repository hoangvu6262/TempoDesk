import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../lib/api'
import type { AuthenticationApi } from '../api/auth-api'
import { AuthContext } from '../state/AuthContext'
import { ResetPasswordPage } from './ResetPasswordPage'

function fakeApi(overrides: Partial<AuthenticationApi> = {}): AuthenticationApi {
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

function renderPage(api: AuthenticationApi, entry: string) {
  render(
    <AuthContext
      value={{
        status: 'anonymous',
        api,
        login: vi.fn(),
        logoutLocal: vi.fn(),
      }}
    >
      <MemoryRouter initialEntries={[entry]}>
        <ResetPasswordPage />
      </MemoryRouter>
    </AuthContext>,
  )
}

describe('ResetPasswordPage', () => {
  it('rejects a missing token without making a request', () => {
    const resetPassword = vi.fn()
    renderPage(fakeApi({ resetPassword }), '/reset-password')
    expect(screen.getByRole('alert')).toHaveTextContent(
      'The reset link is invalid or expired.',
    )
    expect(resetPassword).not.toHaveBeenCalled()
  })

  it('resets with the URL token and shows the login action', async () => {
    const user = userEvent.setup()
    const resetPassword = vi.fn().mockResolvedValue(undefined)
    renderPage(
      fakeApi({ resetPassword }),
      '/reset-password?token=reset%2Ftoken',
    )

    await user.type(screen.getByLabelText(/^New password/), 'NewPassword12')
    await user.type(
      screen.getByLabelText(/^Confirm password/),
      'NewPassword12',
    )
    await user.click(screen.getByRole('button', { name: 'Reset password' }))

    expect(resetPassword).toHaveBeenCalledWith(
      'reset/token',
      'NewPassword12',
    )
    expect(await screen.findByRole('link', { name: 'Sign in' })).toHaveAttribute(
      'href',
      '/login',
    )
  })

  it('maps server password validation to the field', async () => {
    const user = userEvent.setup()
    const resetPassword = vi.fn().mockRejectedValue(
      new ApiError('Invalid', {
        errors: { NewPassword: ['Use a stronger password.'] },
      }),
    )
    renderPage(fakeApi({ resetPassword }), '/reset-password?token=valid')
    await user.type(screen.getByLabelText(/^New password/), 'NewPassword12')
    await user.type(
      screen.getByLabelText(/^Confirm password/),
      'NewPassword12',
    )
    await user.click(screen.getByRole('button', { name: 'Reset password' }))

    expect(await screen.findByText('Use a stronger password.')).toBeVisible()
  })
})
