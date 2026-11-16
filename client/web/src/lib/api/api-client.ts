import axios, { type InternalAxiosRequestConfig } from 'axios'
import { API_ERROR_MESSAGE } from '../../constants/errors'
import {
  DEFAULT_API_TIMEOUT_MS,
  JSON_ACCEPT_HEADER,
} from '../../constants/http'
import { ApiError, toApiError } from './api-error'
import { assertSafeApiPath } from './api-path'

const AUTH_RECOVERY_EXCLUDED_PATHS = new Set([
  '/api/auth/login',
  '/api/auth/refresh',
])

type AuthRetryConfig = InternalAxiosRequestConfig & {
  authRetry?: boolean
}

export function createApiClient(
  baseUrl: string,
  requestHeaders?: () => HeadersInit,
  onUnauthorized?: () => Promise<boolean>,
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
        new ApiError(API_ERROR_MESSAGE.invalidTimeout, {
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
    async (error: unknown) => {
      const apiError = toApiError(error)
      if (apiError.status !== 401 || !axios.isAxiosError(error)) {
        return Promise.reject(apiError)
      }

      const config = error.config as AuthRetryConfig | undefined
      if (
        !config ||
        config.authRetry ||
        AUTH_RECOVERY_EXCLUDED_PATHS.has(config.url ?? '') ||
        !onUnauthorized
      ) {
        return Promise.reject(apiError)
      }

      config.authRetry = true
      if (!(await onUnauthorized())) return Promise.reject(apiError)

      config.headers.delete('Authorization')
      return api.request(config)
    },
  )

  return api
}
