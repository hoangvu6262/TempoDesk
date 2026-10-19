import { describe, expect, it, vi } from 'vitest'
import { ApiError, createApiClient } from './api-client'

const request = createApiClient('/api', () => ({
  Authorization: 'Bearer test-token',
}))
describe('API boundary', () => {
  it('sends JSON and reserved token headers, and parses a JSON result', async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ id: 1 }))
    vi.stubGlobal('fetch', fetchMock)
    expect(
      await request('/tasks', { method: 'POST', json: { title: 'Test' } }),
    ).toEqual({ id: 1 })
    const [url, options] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/tasks')
    expect(options.headers.get('Authorization')).toBe('Bearer test-token')
    expect(options.headers.get('Content-Type')).toBe('application/json')
    expect(options.body).toBe('{"title":"Test"}')
  })
  it('supports no-content success', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 204 })),
    )
    expect(await request('/tasks')).toBeUndefined()
  })
  it.each([400, 500])(
    'preserves Problem Details for status %s',
    async (status) => {
      vi.stubGlobal(
        'fetch',
        vi.fn().mockResolvedValue(
          Response.json(
            {
              detail: 'Safe detail',
              traceId: 'trace-1',
              errors: { title: ['Required'] },
            },
            {
              status,
              headers: { 'Content-Type': 'application/problem+json' },
            },
          ),
        ),
      )
      await expect(request('/tasks')).rejects.toMatchObject({
        status,
        traceId: 'trace-1',
        detail: 'Safe detail',
        errors: { title: ['Required'] },
        kind: 'http',
      })
    },
  )
  it('does not expose non-problem server text', async () => {
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValue(new Response('internal secrets', { status: 500 })),
    )
    await expect(request('/tasks')).rejects.toMatchObject({
      status: 500,
      detail: 'Không thể hoàn tất yêu cầu.',
    })
  })
  it('rejects malformed successful JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('invalid')))
    await expect(request('/tasks')).rejects.toMatchObject({
      kind: 'invalid-response',
    })
  })
  it.each([
    'https://evil.test',
    '//evil.test',
    '/tasks/../private',
    '/%2e%2e/private',
    '/tasks\\private',
  ])('rejects unsafe request path %s before fetch', async (path) => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    await expect(request(path)).rejects.toBeInstanceOf(ApiError)
    expect(fetchMock).not.toHaveBeenCalled()
  })
  it.each([
    ['/', '/\t/evil.example'],
    ['/api', '/\n../private'],
    ['/api', '/tasks\u007fprivate'],
    ['/api', '/tasks\u0000private'],
    ['/api', '/tasks private'],
  ])(
    'rejects URL-normalized controls at base %s before headers or fetch',
    async (baseUrl, path) => {
      const fetchMock = vi.fn()
      const headers = vi.fn(() => ({ Authorization: 'Bearer test-token' }))
      vi.stubGlobal('fetch', fetchMock)
      await expect(
        createApiClient(baseUrl, headers)(path),
      ).rejects.toMatchObject({ kind: 'invalid-response' })
      expect(headers).not.toHaveBeenCalled()
      expect(fetchMock).not.toHaveBeenCalled()
    },
  )
  function abortingFetch() {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_url, options: RequestInit) =>
          new Promise((_resolve, reject) => {
            if (options.signal?.aborted)
              reject(new DOMException('Aborted', 'AbortError'))
            options.signal?.addEventListener(
              'abort',
              () => reject(new DOMException('Aborted', 'AbortError')),
              { once: true },
            )
          }),
      ),
    )
  }
  it('times out without an API response', async () => {
    vi.useFakeTimers()
    abortingFetch()
    const result = expect(
      request('/tasks', { timeoutMs: 20 }),
    ).rejects.toMatchObject({ kind: 'timeout', status: 0 })
    await vi.advanceTimersByTimeAsync(20)
    await result
  })
  it('honors caller cancellation', async () => {
    abortingFetch()
    const controller = new AbortController()
    const pending = request('/tasks', { signal: controller.signal })
    controller.abort()
    await expect(pending).rejects.toMatchObject({ kind: 'cancelled' })
  })
  it('honors a signal cancelled before the request', async () => {
    abortingFetch()
    await expect(
      request('/tasks', { signal: AbortSignal.abort() }),
    ).rejects.toMatchObject({ kind: 'cancelled' })
  })
  it('normalizes network errors', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockRejectedValue(new TypeError('Network unavailable')),
    )
    await expect(request('/tasks')).rejects.toMatchObject({ kind: 'network' })
  })
})
