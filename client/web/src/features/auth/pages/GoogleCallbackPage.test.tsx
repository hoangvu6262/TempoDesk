import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { expect, it } from 'vitest'
import { GoogleCallbackPage } from './GoogleCallbackPage'

it('does not consume or render callback parameters', () => {
  render(
    <MemoryRouter
      initialEntries={[
        '/auth/google/callback?code=private-code&state=private-state',
      ]}
    >
      <GoogleCallbackPage />
    </MemoryRouter>,
  )

  expect(
    screen.getByText('Google sign-in is not configured'),
  ).toBeVisible()
  expect(screen.queryByText(/private-code|private-state/)).not.toBeInTheDocument()
  expect(localStorage).toHaveLength(0)
  expect(sessionStorage).toHaveLength(0)
})
