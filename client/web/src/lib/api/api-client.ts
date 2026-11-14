import axios from 'axios'
import {
  DEFAULT_API_TIMEOUT_MS,
  JSON_ACCEPT_HEADER,
} from '../../constants/http'
import { ApiError, toApiError } from './api-error'
import { assertSafeApiPath } from './api-path'

export function createApiClient(
  baseUrl: string,
  requestHeaders?: () => HeadersInit,
) {
  const api = axios.create({
    baseURL: baseUrl.replace(/\/$/, ''),
    timeout: DEFAULT_API_TIMEOUT_MS,
    responseType: 'json',
    transitional: { silentJSONParsing: false },
  })

  api.interceptors.request.use((config) => {
    assertSafeApiPath(config.url ?? '')
    if (
      typeof config.timeout !== 'number' ||
      !Number.isFinite(config.timeout) ||
      config.timeout <= 0
    ) {
      throw toApiError(
        new ApiError('Thời gian chờ không hợp lệ.', {
          kind: 'invalid-response',
        }),
      )
    }

    new Headers(requestHeaders?.()).forEach((value, key) => {
      if (!config.headers.has(key)) config.headers.set(key, value)
    })
    config.headers.set('Accept', JSON_ACCEPT_HEADER)
    return config
  })

  api.interceptors.response.use(
    (response) => response,
    (error: unknown) => Promise.reject(toApiError(error)),
  )

  return api
}
