import axios, { AxiosError, AxiosHeaders } from 'axios'
import { PROBLEM_JSON_MEDIA_TYPE } from '../../constants/http'

export type ApiErrorKind =
  'http' | 'network' | 'timeout' | 'cancelled' | 'invalid-response'

export class ApiError extends Error {
  readonly status: number
  readonly traceId?: string
  readonly errors?: Record<string, string[]>
  readonly detail: string
  readonly kind: ApiErrorKind

  constructor(
    detail: string,
    options: {
      status?: number
      traceId?: string
      errors?: Record<string, string[]>
      kind?: ApiErrorKind
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

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null

function readValidationErrors(value: unknown) {
  if (
    !isRecord(value) ||
    !Object.values(value).every(
      (messages) =>
        Array.isArray(messages) &&
        messages.every((message) => typeof message === 'string'),
    )
  ) {
    return undefined
  }
  return value as Record<string, string[]>
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) return error
  if (axios.isCancel(error)) {
    return new ApiError('Yêu cầu đã được hủy.', { kind: 'cancelled' })
  }
  if (!axios.isAxiosError(error)) {
    return new ApiError('Không thể kết nối đến API.', { kind: 'network' })
  }
  if (
    error.code === AxiosError.ECONNABORTED ||
    error.code === AxiosError.ETIMEDOUT
  ) {
    return new ApiError('API phản hồi quá thời gian chờ.', { kind: 'timeout' })
  }

  const { response } = error
  if (!response) {
    return new ApiError('Không thể kết nối đến API.', { kind: 'network' })
  }
  if (
    response.status >= 200 &&
    response.status < 300 &&
    error.code === AxiosError.ERR_BAD_RESPONSE
  ) {
    return new ApiError('Phản hồi API không hợp lệ.', {
      status: response.status,
      kind: 'invalid-response',
    })
  }

  const contentType =
    response.headers instanceof AxiosHeaders
      ? response.headers.get('content-type')
      : (response.headers['content-type'] ?? response.headers['Content-Type'])
  const problem =
    typeof contentType === 'string' &&
    contentType.includes(PROBLEM_JSON_MEDIA_TYPE) &&
    isRecord(response.data)
      ? response.data
      : {}

  return new ApiError(
    typeof problem.detail === 'string'
      ? problem.detail
      : 'Không thể hoàn tất yêu cầu.',
    {
      status: response.status,
      traceId:
        typeof problem.traceId === 'string' ? problem.traceId : undefined,
      errors: readValidationErrors(problem.errors),
    },
  )
}
