import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { ThemeProvider } from './ThemeProvider'
import { useTheme } from './useTheme'

function createMediaQuery(matches: boolean) {
  return {
    matches,
    media: '(prefers-color-scheme: dark)',
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }
}

function ThemeProbe() {
  const { preference, resolvedTheme, setPreference } = useTheme()
  return (
    <>
      <output role="status">{`${preference}:${resolvedTheme}`}</output>
      <button type="button" onClick={() => setPreference('dark')}>
        Dark
      </button>
    </>
  )
}

it('persists an explicit theme and applies it to the document', async () => {
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => createMediaQuery(false)),
  )
  const user = userEvent.setup()

  render(
    <ThemeProvider>
      <ThemeProbe />
    </ThemeProvider>,
  )

  await user.click(screen.getByRole('button', { name: 'Dark' }))

  expect(screen.getByRole('status')).toHaveTextContent('dark:dark')
  expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  expect(localStorage.getItem('tempodesk.theme')).toBe('dark')
})

it('follows the operating system when system preference is selected', () => {
  localStorage.setItem('tempodesk.theme', 'system')
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => createMediaQuery(true)),
  )

  render(
    <ThemeProvider>
      <ThemeProbe />
    </ThemeProvider>,
  )

  expect(screen.getByRole('status')).toHaveTextContent('system:dark')
})
