import { describe, expect, it } from 'vitest'
import { safeReturnPath } from './auth-return-path'

describe('safe return paths', () => {
  it.each(['/app', '/app?view=mine#today'])(
    'allows internal path %s',
    (path) => {
      expect(safeReturnPath(path)).toBe(path)
    },
  )

  it.each([
    'https://evil.example',
    '//evil.example',
    '/\\evil',
    'app',
    '/login',
    '/register',
    '/forgot-password',
    '/reset-password',
    '/verify-email',
    '/auth/google/callback',
  ])('rejects unsafe return path %s', (path) => {
    expect(safeReturnPath(path)).toBe('/app')
  })

  it('uses the supplied fallback', () => {
    expect(safeReturnPath(null, '/')).toBe('/')
  })
})
