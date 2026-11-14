import { render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { Text } from './Text'

it('renders the requested semantic element', () => {
  render(<Text as="span">Secondary copy</Text>)
  expect(screen.getByText('Secondary copy').tagName).toBe('SPAN')
})
