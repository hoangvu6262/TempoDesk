import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../lib/api'
import type { AuthenticationApi } from '../../../api/auth-api'
import { AuthContext } from '../state/AuthContext'
import { VerifyEmailPage } from './VerifyEmailPage'

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
        <VerifyEmailPage />
      </MemoryRouter>
    </AuthContext>,
  )
}

describe('VerifyEmailPage', () => {
  it('verifies the URL token once and links to login', async () => {
    const verifyEmail = vi.fn().mockResolvedValue(undefined)
    renderPage(fakeApi({ verifyEmail }), '/verify-email?token=abc%2F123')

    expect(await screen.findByText('Email verified')).toBeVisible()
    expect(verifyEmail).toHaveBeenCalledTimes(1)
    expect(verifyEmail).toHaveBeenCalledWith('abc/123')
    expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute(
      'href',
      '/login',
    )
  })

  it('rejects a missing token without making a request', async () => {
    const verifyEmail = vi.fn()
    renderPage(fakeApi({ verifyEmail }), '/verify-email')

    expect(
      await screen.findByText('The verification link is invalid or expired.'),
    ).toBeVisible()
    expect(verifyEmail).not.toHaveBeenCalled()
  })

  it('resends verification after a rejected token', async () => {
    const user = userEvent.setup()
    const resendVerification = vi.fn().mockResolvedValue({
      message: 'If the address can be verified, an email will be sent.',
    })
    renderPage(
      fakeApi({
        verifyEmail: vi.fn().mockRejectedValue(
          new ApiError('Invalid', {
            errors: {
              token: ['The verification token is invalid or expired.'],
            },
          }),
        ),
        resendVerification,
      }),
      '/verify-email?token=bad',
    )

    expect(
      await screen.findByText('The verification token is invalid or expired.'),
    ).toBeVisible()
    await user.type(screen.getByLabelText(/^Email/), 'person@example.com')
    await user.click(
      screen.getByRole('button', { name: 'Resend verification' }),
    )

    expect(resendVerification).toHaveBeenCalledWith('person@example.com')
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'If the address can be verified, an email will be sent.',
    )
  })
})
