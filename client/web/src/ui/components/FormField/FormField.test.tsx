import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { Input } from '../Input/Input'
import { FormField } from './FormField'

it('connects the label, description, and error to the control', () => {
  render(
    <FormField label="Email" description="Use your work email" error="Required">
      {(props) => <Input {...props} />}
    </FormField>,
  )
  const input = screen.getByRole('textbox', { name: 'Email' })
  expect(input).toHaveAccessibleDescription('Use your work email Required')
  expect(input).toHaveAttribute('aria-invalid', 'true')
})

it('announces required fields in English', () => {
  render(
    <FormField label="Email" required>
      {(props) => <Input {...props} />}
    </FormField>,
  )
  expect(screen.getByRole('textbox', { name: 'Email, required' })).toBeVisible()
})
