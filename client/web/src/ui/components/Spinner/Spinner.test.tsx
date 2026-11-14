import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { Spinner } from './Spinner'

it('is decorative by default and named when a label is provided', () => {
  const { rerender } = render(<Spinner />)
  expect(screen.queryByRole('status')).not.toBeInTheDocument()

  rerender(<Spinner label="Đang tải" />)
  expect(screen.getByRole('status', { name: 'Đang tải' })).toBeVisible()
})
