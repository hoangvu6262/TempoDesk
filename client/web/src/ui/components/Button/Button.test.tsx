import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { Button } from './Button'

it('prevents activation while loading and keeps its accessible name', async () => {
  const onClick = vi.fn()
  const user = userEvent.setup()
  render(
    <Button loading onClick={onClick}>
      Save
    </Button>,
  )
  const button = screen.getByRole('button', { name: 'Save' })
  expect(button).toBeDisabled()
  expect(button).toHaveAttribute('aria-busy', 'true')
  await user.click(button)
  expect(onClick).not.toHaveBeenCalled()
})
