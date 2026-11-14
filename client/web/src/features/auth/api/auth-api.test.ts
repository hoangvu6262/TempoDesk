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
      config.url === '/auth/login' || config.url === '/auth/refresh'
        ? { accessToken: 'access', tokenType: 'Bearer', expiresIn: 300 }
        : config.url === '/auth/register' ||
            config.url === '/auth/resend-verification' ||
            config.url === '/auth/forgot-password'
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
    ['post', '/auth/register'],
    ['post', '/auth/verify-email'],
    ['post', '/auth/resend-verification'],
    ['post', '/auth/login'],
    ['post', '/auth/forgot-password'],
    ['post', '/auth/reset-password'],
    ['post', '/auth/refresh'],
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
