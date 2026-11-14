import {
  AxiosError,
  AxiosHeaders,
  CanceledError,
  type AxiosAdapter,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios'
import { describe, expect, it, vi } from 'vitest'
import { ApiError, createApiClient } from './api-client'

function response(
  config: InternalAxiosRequestConfig,
  data: unknown,
  status = 200,
  headers: Record<string, string> = { 'Content-Type': 'application/json' },
): AxiosResponse {
  return {
    config,
    data,
    headers: AxiosHeaders.from(headers),
    status,
    statusText: '',
  }
}

function httpError(
  config: InternalAxiosRequestConfig,
  data: unknown,
  status: number,
  contentType = 'application/problem+json',
) {
  const result = response(config, data, status, { 'Content-Type': contentType })
  return new AxiosError(
    `Request failed with status code ${status}`,
    AxiosError.ERR_BAD_RESPONSE,
    config,
    undefined,
    result,
  )
}

function setAdapter(adapter: AxiosAdapter) {
  const headers = vi.fn(() => ({ Authorization: 'Bearer test-token' }))
  const request = createApiClient('/api', headers)
  request.defaults.adapter = adapter
  return { headers, request }
}

describe('API boundary', () => {
  it('sends JSON and reserved token headers, and parses a JSON result', async () => {
    const adapter = vi.fn<AxiosAdapter>(async (config) =>
      response(config, { id: 1 }),
    )
    const { request } = setAdapter(adapter)

    const result = await request.post<{ id: number }>('/tasks', {
      title: 'Test',
    })

    expect(result.data).toEqual({ id: 1 })
    const config = adapter.mock.calls[0][0]
    expect(config.baseURL).toBe('/api')
    expect(config.url).toBe('/tasks')
    expect(config.headers.get('Authorization')).toBe('Bearer test-token')
    expect(config.headers.get('Content-Type')).toContain('application/json')
    expect(config.data).toBe('{"title":"Test"}')
  })

  it('supports no-content success', async () => {
    const { request } = setAdapter(async (config) =>
      response(config, undefined, 204, {}),
    )
    expect((await request.get('/tasks')).data).toBeUndefined()
  })

  it.each([400, 500])(
    'preserves Problem Details for status %s',
    async (status) => {
      const { request } = setAdapter(async (config) => {
        throw httpError(
          config,
          {
            detail: 'Safe detail',
            traceId: 'trace-1',
            errors: { title: ['Required'] },
          },
          status,
        )
      })

      await expect(request.get('/tasks')).rejects.toMatchObject({
        status,
        traceId: 'trace-1',
        detail: 'Safe detail',
        errors: { title: ['Required'] },
        kind: 'http',
      })
    },
  )

  it('does not expose non-problem server text', async () => {
    const { request } = setAdapter(async (config) => {
      throw httpError(config, 'internal secrets', 500, 'text/plain')
    })
    await expect(request.get('/tasks')).rejects.toMatchObject({
      status: 500,
      detail: 'Unable to complete the request.',
    })
  })

  it('rejects malformed successful JSON', async () => {
    const { request } = setAdapter(async (config) =>
      response(config, 'invalid'),
    )
    await expect(request.get('/tasks')).rejects.toMatchObject({
      kind: 'invalid-response',
    })
  })

  it.each([
    'https://evil.test',
    '//evil.test',
    '/tasks/../private',
    '/%2e%2e/private',
    '/tasks\\private',
  ])(
    'rejects unsafe request path %s before headers or adapter',
    async (path) => {
      const adapter = vi.fn<AxiosAdapter>()
      const { headers, request } = setAdapter(adapter)
      await expect(request.get(path)).rejects.toBeInstanceOf(ApiError)
      expect(headers).not.toHaveBeenCalled()
      expect(adapter).not.toHaveBeenCalled()
    },
  )

  it.each([
    '/\t/evil.example',
    '/\n../private',
    '/tasks\u007fprivate',
    '/tasks\u0000private',
    '/tasks private',
  ])('rejects URL-normalized controls in %s', async (path) => {
    const adapter = vi.fn<AxiosAdapter>()
    const { headers, request } = setAdapter(adapter)
    await expect(request.get(path)).rejects.toMatchObject({
      kind: 'invalid-response',
    })
    expect(headers).not.toHaveBeenCalled()
    expect(adapter).not.toHaveBeenCalled()
  })

  it('rejects an invalid timeout before the adapter', async () => {
    const adapter = vi.fn<AxiosAdapter>()
    const { request } = setAdapter(adapter)
    await expect(request.get('/tasks', { timeout: 0 })).rejects.toMatchObject({
      kind: 'invalid-response',
    })
    expect(adapter).not.toHaveBeenCalled()
  })

  it('normalizes timeout errors', async () => {
    const { request } = setAdapter(async (config) => {
      throw new AxiosError('timeout', AxiosError.ECONNABORTED, config)
    })
    await expect(request.get('/tasks')).rejects.toMatchObject({
      kind: 'timeout',
      status: 0,
    })
  })

  it('honors caller cancellation', async () => {
    const { request } = setAdapter(
      (config) =>
        new Promise((_resolve, reject) => {
          config.signal?.addEventListener?.(
            'abort',
            () => reject(new CanceledError(undefined, config)),
            { once: true },
          )
        }),
    )
    const controller = new AbortController()
    const pending = request.get('/tasks', { signal: controller.signal })
    controller.abort()
    await expect(pending).rejects.toMatchObject({ kind: 'cancelled' })
  })

  it('honors a signal cancelled before the request', async () => {
    const adapter = vi.fn<AxiosAdapter>()
    const { request } = setAdapter(adapter)
    await expect(
      request.get('/tasks', { signal: AbortSignal.abort() }),
    ).rejects.toMatchObject({ kind: 'cancelled' })
    expect(adapter).not.toHaveBeenCalled()
  })

  it('normalizes network errors', async () => {
    const { request } = setAdapter(async (config) => {
      throw new AxiosError(
        'Network unavailable',
        AxiosError.ERR_NETWORK,
        config,
      )
    })
    await expect(request.get('/tasks')).rejects.toMatchObject({
      kind: 'network',
    })
  })
})
