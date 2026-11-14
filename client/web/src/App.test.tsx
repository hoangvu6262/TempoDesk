import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { expect, it, vi } from 'vitest'
import App from './App'
import { ErrorBoundary } from './app/ErrorBoundary'
import { ThemeProvider } from './ui'

function matchMedia() {
  return {
    matches: false,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  }
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
  expect(
    screen.getByRole('heading', { name: 'Không tìm thấy trang' }),
  ).toBeVisible()
  expect(screen.getByRole('link', { name: 'Về trang chủ →' })).toHaveAttribute(
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
  expect(screen.getByRole('alert')).toHaveTextContent('Đã có lỗi xảy ra')
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
    'Cấu hình ứng dụng không hợp lệ',
  )
  vi.unstubAllEnvs()
})
