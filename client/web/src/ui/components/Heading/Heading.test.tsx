import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { Heading } from './Heading'

it('keeps semantic level independent from visual size', () => {
  render(
    <Heading as="h2" size="1">
      Account
    </Heading>,
  )
  expect(
    screen.getByRole('heading', { level: 2, name: 'Account' }),
  ).toBeVisible()
})
