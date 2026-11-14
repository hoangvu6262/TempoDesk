import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { AuthLayout } from './AuthLayout'

it('provides named authentication content and optional footer', () => {
  render(
    <AuthLayout
      title="Sign in"
      description="Continue with your TempoDesk account"
      footer={<a href="/privacy">Privacy</a>}
    >
      <form aria-label="Sign in" />
    </AuthLayout>,
  )
  expect(screen.getByRole('heading', { name: 'Sign in' })).toBeVisible()
  expect(screen.getByRole('form', { name: 'Sign in' })).toBeVisible()
  expect(screen.getByRole('link', { name: 'Privacy' })).toBeVisible()
  expect(screen.getByRole('link', { name: 'TempoDesk home' })).toBeVisible()
})
