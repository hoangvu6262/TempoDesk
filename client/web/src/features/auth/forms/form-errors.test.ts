import { describe, expect, it } from 'vitest'
import { ApiError } from '../../../lib/api'
import {
  fieldError,
  pageError,
  validateConfirmation,
  validateEmail,
  validatePassword,
} from './form-errors'

describe('authentication form errors', () => {
  it.each([
    ['', 'Email is required.'],
    ['invalid', 'Enter a valid email address.'],
    [
      `${'a'.repeat(245)}@example.com`,
      'Email must be 256 characters or fewer.',
    ],
  ])('validates email %j', (value, expected) => {
    expect(validateEmail(value)).toBe(expected)
  })

  it('accepts a valid email address', () => {
    expect(validateEmail('person@example.com')).toBeUndefined()
  })

  it('validates password length and confirmation', () => {
    expect(validatePassword('short')).toBe(
      'Password must be at least 12 characters.',
    )
    expect(validatePassword('a'.repeat(1025))).toBe(
      'Password must be 1024 characters or fewer.',
    )
    expect(validateConfirmation('one', 'two')).toBe('Passwords do not match.')
  })

  it('matches Problem Details field names case-insensitively', () => {
    const error = new ApiError('Invalid', {
      errors: {
        Email: ['Server email error'],
        NewPassword: ['Server password error'],
      },
    })
    expect(fieldError(error, 'email')).toBe('Server email error')
    expect(fieldError(error, 'newPassword')).toBe('Server password error')
    expect(pageError(error, ['email', 'newPassword'])).toBeUndefined()
  })

  it('uses unmatched validation and safe general error details', () => {
    expect(
      pageError(
        new ApiError('Invalid', { errors: { token: ['Invalid token'] } }),
        ['email'],
      ),
    ).toBe('Invalid token')
    expect(pageError(new ApiError('Try again'), [])).toBe('Try again')
    expect(pageError(new Error('private'), [])).toBe(
      'Unable to complete the request.',
    )
  })
})
