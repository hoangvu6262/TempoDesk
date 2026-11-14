import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it } from 'vitest'
import { PasswordInput } from './PasswordInput'

it('reveals the existing password without losing its value', async () => {
  const user = userEvent.setup()
  render(<PasswordInput aria-label="Password" />)
  const input = screen.getByLabelText('Password')
  await user.type(input, 'secret')
  await user.click(screen.getByRole('button', { name: 'Show password' }))
  expect(input).toHaveAttribute('type', 'text')
  expect(input).toHaveValue('secret')
  expect(screen.getByRole('button', { name: 'Hide password' })).toBeVisible()
})
