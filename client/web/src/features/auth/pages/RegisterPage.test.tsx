import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../lib/api'
import type { AcceptedResponse, AuthenticationApi } from '../api/auth-api'
import { AuthContext } from '../state/AuthContext'
import { RegisterPage } from './RegisterPage'

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
        <RegisterPage />
      </MemoryRouter>
    </AuthContext>,
  )
}

describe('RegisterPage', () => {
  it('validates before registering and shows the neutral success', async () => {
    const user = userEvent.setup()
    const register = vi.fn().mockResolvedValue({
      message: 'If the address can be verified, an email will be sent.',
    })
    renderPage(fakeApi({ register }))

    await user.click(screen.getByRole('button', { name: 'Create account' }))
    expect(screen.getByText('Email is required.')).toBeVisible()
    expect(register).not.toHaveBeenCalled()

    await user.type(screen.getByLabelText(/^Email/), 'person@example.com')
    await user.type(screen.getByLabelText(/^Password/), 'LongPassword1')
    await user.type(screen.getByLabelText(/^Confirm password/), 'LongPassword1')
    await user.click(screen.getByRole('button', { name: 'Create account' }))

    expect(register).toHaveBeenCalledWith('person@example.com', 'LongPassword1')
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'If the address can be verified, an email will be sent.',
    )
  })

  it('maps server validation to the matching field', async () => {
    const user = userEvent.setup()
    const register = vi.fn().mockRejectedValue(
      new ApiError('Invalid', {
        errors: { Email: ['This email cannot be used.'] },
      }),
    )
    renderPage(fakeApi({ register }))

    await user.type(screen.getByLabelText(/^Email/), 'person@example.com')
    await user.type(screen.getByLabelText(/^Password/), 'LongPassword1')
    await user.type(screen.getByLabelText(/^Confirm password/), 'LongPassword1')
    await user.click(screen.getByRole('button', { name: 'Create account' }))

    expect(await screen.findByText('This email cannot be used.')).toBeVisible()
  })

  it('prevents duplicate submission while pending', async () => {
    const user = userEvent.setup()
    const register = vi.fn(() => new Promise<AcceptedResponse>(() => {}))
    renderPage(fakeApi({ register }))
    await user.type(screen.getByLabelText(/^Email/), 'person@example.com')
    await user.type(screen.getByLabelText(/^Password/), 'LongPassword1')
    await user.type(screen.getByLabelText(/^Confirm password/), 'LongPassword1')

    const submit = screen.getByRole('button', { name: 'Create account' })
    await user.click(submit)
    await user.click(submit)

    expect(register).toHaveBeenCalledTimes(1)
    expect(submit).toBeDisabled()
  })
})
