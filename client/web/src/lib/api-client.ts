export class ApiError extends Error {
  readonly status: number
  readonly traceId?: string
  readonly errors?: Record<string, string[]>
  readonly detail: string
  readonly kind:
    'http' | 'network' | 'timeout' | 'cancelled' | 'invalid-response'
  constructor(
    detail: string,
    options: {
      status?: number
      traceId?: string
      errors?: Record<string, string[]>
      kind?: ApiError['kind']
    } = {},
  ) {
    super(detail)
    this.name = 'ApiError'
    this.detail = detail
    this.status = options.status ?? 0
    this.traceId = options.traceId
    this.errors = options.errors
    this.kind = options.kind ?? 'http'
  }
}
type RequestOptions = Omit<RequestInit, 'body'> & {
  json?: unknown
  timeoutMs?: number
}
const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null

export function createApiClient(
  baseUrl: string,
  requestHeaders?: () => HeadersInit,
) {
  return async function request<T>(
    path: string,
    options: RequestOptions = {},
  ): Promise<T | undefined> {
    // Keep future token headers under the configured API origin and base path.
    if (
      !path.startsWith('/') ||
      path.startsWith('//') ||
      /[\s\p{Cc}\\?#%]/u.test(path) ||
      path.split('/').some((part) => part === '..' || part === '.')
    )
      throw new ApiError('Đường dẫn API không hợp lệ.', {
        kind: 'invalid-response',
      })
    const { json, timeoutMs = 10_000, signal, ...init } = options
    if (!Number.isFinite(timeoutMs) || timeoutMs <= 0)
      throw new ApiError('Thời gian chờ không hợp lệ.', {
        kind: 'invalid-response',
      })
    const controller = new AbortController()
    let timedOut = false
    const cancel = () => controller.abort(signal?.reason)
    signal?.addEventListener('abort', cancel, { once: true })
    if (signal?.aborted) cancel()
    const timer = setTimeout(() => {
      timedOut = true
      controller.abort()
    }, timeoutMs)
    try {
      const headers = new Headers(requestHeaders?.())
      new Headers(init.headers).forEach((value, key) => headers.set(key, value))
      headers.set('Accept', 'application/json, application/problem+json')
      if (json !== undefined) headers.set('Content-Type', 'application/json')
      const response = await fetch(`${baseUrl.replace(/\/$/, '')}${path}`, {
        ...init,
        headers,
        body: json === undefined ? undefined : JSON.stringify(json),
        signal: controller.signal,
      })
      const body = await response.text()
      let data: unknown
      if (body) {
        try {
          data = JSON.parse(body)
        } catch {
          if (response.ok)
            throw new ApiError('Phản hồi API không hợp lệ.', {
              status: response.status,
              kind: 'invalid-response',
            })
        }
      }
      if (!response.ok) {
        const problem =
          response.headers
            .get('content-type')
            ?.includes('application/problem+json') && isRecord(data)
            ? data
            : {}
        const errors =
          isRecord(problem.errors) &&
          Object.values(problem.errors).every(
            (value) =>
              Array.isArray(value) &&
              value.every((item) => typeof item === 'string'),
          )
            ? (problem.errors as Record<string, string[]>)
            : undefined
        throw new ApiError(
          typeof problem.detail === 'string'
            ? problem.detail
            : 'Không thể hoàn tất yêu cầu.',
          {
            status: response.status,
            traceId:
              typeof problem.traceId === 'string' ? problem.traceId : undefined,
            errors,
          },
        )
      }
      return data as T | undefined
    } catch (error) {
      if (controller.signal.aborted)
        throw new ApiError(
          timedOut ? 'API phản hồi quá thời gian chờ.' : 'Yêu cầu đã được hủy.',
          { kind: timedOut ? 'timeout' : 'cancelled' },
        )
      if (error instanceof ApiError) throw error
      throw new ApiError('Không thể kết nối đến API.', { kind: 'network' })
    } finally {
      clearTimeout(timer)
      signal?.removeEventListener('abort', cancel)
    }
  }
}
