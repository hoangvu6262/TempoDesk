import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { expect, it, vi } from 'vitest'
import App from './App'
import { ErrorBoundary } from './app/ErrorBoundary'
import { ThemeProvider } from './ui'
import type { AuthenticationApi } from './features/auth/api/auth-api'
import { AuthContext, type AuthStatus } from './features/auth/state/AuthContext'

function matchMedia() {
  return {
    matches: false,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  }
}

const api = {} as AuthenticationApi

function renderApp(path: string, status: AuthStatus) {
  render(
    <AuthContext value={{ status, api, login: vi.fn(), logoutLocal: vi.fn() }}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </AuthContext>,
  )
}

it('exposes the UI Kit in development', () => {
  vi.stubGlobal('matchMedia', vi.fn(matchMedia))
  render(
    <ThemeProvider>
      <MemoryRouter initialEntries={['/ui-kit']}>
        <App />
      </MemoryRouter>
    </ThemeProvider>,
  )
  expect(
    screen.getByRole('heading', { name: 'TempoDesk UI Kit' }),
  ).toBeVisible()
})
it('renders a useful not-found route', () => {
  render(
    <MemoryRouter initialEntries={['/unknown']}>
      <App />
    </MemoryRouter>,
  )
  expect(screen.getByRole('heading', { name: 'Page not found' })).toBeVisible()
  expect(screen.getByRole('link', { name: 'Back to home →' })).toHaveAttribute(
    'href',
    '/',
  )
})
it('catches unhandled rendering errors without showing their content', () => {
  vi.spyOn(console, 'error').mockImplementation(() => {})
  function Broken(): never {
    throw new Error('private diagnostic')
  }
  render(
    <ErrorBoundary>
      <Broken />
    </ErrorBoundary>,
  )
  expect(screen.getByRole('alert')).toHaveTextContent('Something went wrong')
  expect(screen.queryByText('private diagnostic')).not.toBeInTheDocument()
})
it('handles invalid environment configuration gracefully', () => {
  vi.stubEnv('VITE_API_BASE_URL', 'unsafe')
  render(
    <MemoryRouter>
      <App />
    </MemoryRouter>,
  )
  expect(screen.getByRole('alert')).toHaveTextContent(
    'Application configuration is invalid',
  )
  vi.unstubAllEnvs()
})

it.each([
  ['/register', 'Create your account'],
  ['/login', 'Sign in'],
  ['/forgot-password', 'Forgot your password?'],
  ['/reset-password', 'Reset your password'],
  ['/auth/google/callback', 'Google sign-in is not configured'],
])('renders %s', (path, heading) => {
  renderApp(path, 'anonymous')
  expect(screen.getByRole('heading', { name: heading })).toBeVisible()
})

it('protects /app', async () => {
  renderApp('/app', 'anonymous')
  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeVisible()
})

it('does not render /app while authentication is initializing', () => {
  renderApp('/app', 'initializing')
  expect(screen.getByRole('status')).toHaveTextContent('Checking your session')
  expect(
    screen.queryByRole('heading', { name: 'Your workspace' }),
  ).not.toBeInTheDocument()
})

it('renders /app after authentication', () => {
  renderApp('/app', 'authenticated')
  expect(screen.getByRole('heading', { name: 'Your workspace' })).toBeVisible()
})
