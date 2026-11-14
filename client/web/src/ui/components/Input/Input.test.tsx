import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { Input } from './Input'

it('forwards native input attributes', () => {
  render(<Input aria-label="Email" required disabled />)
  expect(screen.getByRole('textbox', { name: 'Email' })).toBeDisabled()
  expect(screen.getByRole('textbox', { name: 'Email' })).toBeRequired()
})
