import { IconX } from '@tabler/icons-react'
import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { IconButton } from './IconButton'

it('exposes its required accessible name', () => {
  render(
    <IconButton aria-label="Close">
      <IconX />
    </IconButton>,
  )
  expect(screen.getByRole('button', { name: 'Close' })).toBeVisible()
})
