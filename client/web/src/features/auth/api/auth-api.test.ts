import type {
  AxiosAdapter,
  AxiosResponse,
  InternalAxiosRequestConfig,
} from 'axios'
import { expect, it } from 'vitest'
import { createApiClient } from '../../../lib/api'
import { createAuthenticationApi } from './auth-api'

function response(
  config: InternalAxiosRequestConfig,
  data: unknown,
  status = 200,
): AxiosResponse {
  return { config, data, headers: {}, status, statusText: '' }
}

it('uses the existing authentication contracts', async () => {
  const calls: Array<{ method?: string; url?: string; data?: string }> = []
  const client = createApiClient('/api')
  client.defaults.adapter = (async (config) => {
    calls.push({ method: config.method, url: config.url, data: config.data })
    const data =
      config.url === '/api/auth/login' || config.url === '/api/auth/refresh'
        ? { accessToken: 'access', tokenType: 'Bearer', expiresIn: 300 }
        : config.url === '/api/auth/register' ||
            config.url === '/api/auth/resend-verification' ||
            config.url === '/api/auth/forgot-password'
          ? { message: 'Accepted' }
          : undefined
    return response(config, data, data ? 200 : 204)
  }) as AxiosAdapter
  const api = createAuthenticationApi(client)

  await api.register('person@example.com', 'LongPassword1')
  await api.verifyEmail('verification-token')
  await api.resendVerification('person@example.com')
  await api.login('person@example.com', 'LongPassword1')
  await api.forgotPassword('person@example.com')
  await api.resetPassword('reset-token', 'NewPassword12')
  await api.refresh()

  expect(calls.map(({ method, url }) => [method, url])).toEqual([
    ['post', '/api/auth/register'],
    ['post', '/api/auth/verify-email'],
    ['post', '/api/auth/resend-verification'],
    ['post', '/api/auth/login'],
    ['post', '/api/auth/forgot-password'],
    ['post', '/api/auth/reset-password'],
    ['post', '/api/auth/refresh'],
  ])
  expect(JSON.parse(calls[0].data!)).toEqual({
    email: 'person@example.com',
    password: 'LongPassword1',
  })
  expect(JSON.parse(calls[5].data!)).toEqual({
    token: 'reset-token',
    newPassword: 'NewPassword12',
  })
})
