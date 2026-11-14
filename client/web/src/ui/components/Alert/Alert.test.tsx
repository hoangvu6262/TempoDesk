import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { Alert } from './Alert'

it('does not create a live region unless requested', () => {
  const { rerender } = render(<Alert variant="error">Invalid</Alert>)
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  rerender(
    <Alert variant="error" role="alert">
      Invalid
    </Alert>,
  )
  expect(screen.getByRole('alert')).toHaveTextContent('Invalid')
})
