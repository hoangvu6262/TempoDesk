import { render } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { Checkbox } from './Checkbox'

it('reports a keyboard toggle without owning checked state', async () => {
  const onCheckedChange = vi.fn()
  const user = userEvent.setup()
  render(
    <Checkbox
      checked={false}
      onCheckedChange={onCheckedChange}
      label="Remember me"
    />,
  )
  await user.tab()
  await user.keyboard('[Space]')
  expect(onCheckedChange).toHaveBeenCalledWith(true)
})
