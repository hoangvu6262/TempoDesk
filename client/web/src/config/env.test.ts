import { describe, expect, it } from 'vitest'
import { readClientConfig } from './env'
describe('client environment', () => {
  it('defaults to same-origin proxies', () =>
    expect(readClientConfig({})).toEqual({
      apiBaseUrl: '/api',
      signalRBaseUrl: '/hubs',
    }))
  it('allows absolute HTTP(S) URLs and removes trailing slash', () =>
    expect(
      readClientConfig({
        VITE_API_BASE_URL: 'https://api.example.test/v1/',
        VITE_SIGNALR_BASE_URL: '/hubs/',
      }),
    ).toEqual({
      apiBaseUrl: 'https://api.example.test/v1',
      signalRBaseUrl: '/hubs',
    }))
  it.each([
    '',
    '//evil.test',
    'javascript:alert(1)',
    'ftp://example.test',
    'https:example.test',
    'https://user:secret@example.test',
    '/api?secret=x',
    '/api#hash',
    '/api/../other',
    '/api\\other',
    ' /api',
    '/%2f/evil',
  ])('rejects unsafe config without leaking its value: %s', (value) => {
    expect(() => readClientConfig({ VITE_API_BASE_URL: value })).toThrow(
      'VITE_API_BASE_URL phải là',
    )
    expect(() => readClientConfig({ VITE_SIGNALR_BASE_URL: value })).toThrow(
      'VITE_SIGNALR_BASE_URL phải là',
    )
  })
})
