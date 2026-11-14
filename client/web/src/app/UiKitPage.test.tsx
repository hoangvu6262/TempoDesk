import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { ThemeProvider } from '../ui'
import { UiKitPage } from './UiKitPage'

it('shows supported components and changes theme preference', async () => {
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({
      matches: false,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })),
  )
  const user = userEvent.setup()
  render(
    <ThemeProvider>
      <UiKitPage />
    </ThemeProvider>,
  )

  expect(
    screen.getByRole('heading', { name: 'TempoDesk UI Kit' }),
  ).toBeVisible()
  expect(screen.getByRole('button', { name: 'Đang lưu' })).toBeDisabled()
  expect(screen.getByRole('textbox', { name: 'Invalid email' })).toBeInvalid()
  await user.click(screen.getByRole('button', { name: 'Dark' }))
  expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
})
