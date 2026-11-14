import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { StrictMode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import type {
  AccessTokenResponse,
  AuthenticationApi,
} from '../api/auth-api'
import { AuthProvider } from './AuthProvider'
import { useAuth } from './AuthContext'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, reject, resolve }
}

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

function StatusProbe() {
  const { login, status } = useAuth()
  return (
    <>
      <span>{status}</span>
      <button
        type="button"
        onClick={() => void login('person@example.com', 'password')}
      >
        Test login
      </button>
    </>
  )
}

const token = (
  accessToken: string,
  expiresIn = 300,
): AccessTokenResponse => ({ accessToken, tokenType: 'Bearer', expiresIn })

describe('AuthProvider', () => {
  it('hides authentication until startup refresh resolves', async () => {
    const pending = deferred<AccessTokenResponse>()
    const api = fakeApi({ refresh: vi.fn(() => pending.promise) })
    render(
      <AuthProvider api={api}>
        <StatusProbe />
      </AuthProvider>,
    )

    expect(screen.getByText('initializing')).toBeVisible()
    await act(async () => pending.resolve(token('secret')))

    expect(screen.getByText('authenticated')).toBeVisible()
    expect(screen.queryByText('secret')).not.toBeInTheDocument()
  })

  it('becomes anonymous when startup refresh fails', async () => {
    const api = fakeApi({
      refresh: vi.fn().mockRejectedValue(new Error('401')),
    })
    render(
      <AuthProvider api={api}>
        <StatusProbe />
      </AuthProvider>,
    )

    expect(await screen.findByText('anonymous')).toBeVisible()
  })

  it('performs one effective startup refresh under Strict Mode', async () => {
    const refresh = vi.fn().mockResolvedValue(token('secret'))
    render(
      <StrictMode>
        <AuthProvider api={fakeApi({ refresh })}>
          <StatusProbe />
        </AuthProvider>
      </StrictMode>,
    )

    expect(await screen.findByText('authenticated')).toBeVisible()
    expect(refresh).toHaveBeenCalledTimes(1)
  })

  it('clears the session when scheduled refresh fails', async () => {
    vi.useFakeTimers()
    const refresh = vi
      .fn()
      .mockResolvedValueOnce(token('first', 31))
      .mockRejectedValueOnce(new Error('expired'))
    render(
      <AuthProvider api={fakeApi({ refresh })}>
        <StatusProbe />
      </AuthProvider>,
    )

    await act(async () => {})
    expect(screen.getByText('authenticated')).toBeVisible()
    await act(async () => vi.advanceTimersByTimeAsync(1_000))

    expect(refresh).toHaveBeenCalledTimes(2)
    expect(screen.getByText('anonymous')).toBeVisible()
    expect(localStorage).toHaveLength(0)
    expect(sessionStorage).toHaveLength(0)
  })

  it('stays authenticated after scheduled refresh succeeds', async () => {
    vi.useFakeTimers()
    const refresh = vi
      .fn()
      .mockResolvedValueOnce(token('first', 31))
      .mockResolvedValueOnce(token('second', 60))
    render(
      <AuthProvider api={fakeApi({ refresh })}>
        <StatusProbe />
      </AuthProvider>,
    )

    await act(async () => {})
    await act(async () => vi.advanceTimersByTimeAsync(1_000))

    expect(refresh).toHaveBeenCalledTimes(2)
    expect(screen.getByText('authenticated')).toBeVisible()
  })

  it('logs in without exposing or persisting the access token', async () => {
    const login = vi.fn().mockResolvedValue(token('private-token'))
    const api = fakeApi({
      refresh: vi.fn().mockRejectedValue(new Error('anonymous')),
      login,
    })
    render(
      <AuthProvider api={api}>
        <StatusProbe />
      </AuthProvider>,
    )
    expect(await screen.findByText('anonymous')).toBeVisible()

    await userEvent.click(screen.getByRole('button', { name: 'Test login' }))

    expect(login).toHaveBeenCalledWith('person@example.com', 'password')
    expect(await screen.findByText('authenticated')).toBeVisible()
    expect(screen.queryByText('private-token')).not.toBeInTheDocument()
    expect(localStorage).toHaveLength(0)
    expect(sessionStorage).toHaveLength(0)
  })
})
