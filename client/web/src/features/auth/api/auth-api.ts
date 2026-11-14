import type { AxiosInstance } from 'axios'

export type AccessTokenResponse = {
  accessToken: string
  tokenType: string
  expiresIn: number
}

export type AcceptedResponse = { message: string }

export type AuthenticationApi = {
  register(email: string, password: string): Promise<AcceptedResponse>
  verifyEmail(token: string): Promise<void>
  resendVerification(email: string): Promise<AcceptedResponse>
  login(email: string, password: string): Promise<AccessTokenResponse>
  forgotPassword(email: string): Promise<AcceptedResponse>
  resetPassword(token: string, newPassword: string): Promise<void>
  refresh(): Promise<AccessTokenResponse>
}

export function createAuthenticationApi(
  client: AxiosInstance,
): AuthenticationApi {
  return {
    register: async (email, password) =>
      (
        await client.post<AcceptedResponse>('/auth/register', {
          email,
          password,
        })
      ).data,
    verifyEmail: async (token) => {
      await client.post('/auth/verify-email', { token })
    },
    resendVerification: async (email) =>
      (
        await client.post<AcceptedResponse>('/auth/resend-verification', {
          email,
        })
      ).data,
    login: async (email, password) =>
      (
        await client.post<AccessTokenResponse>('/auth/login', {
          email,
          password,
        })
      ).data,
    forgotPassword: async (email) =>
      (await client.post<AcceptedResponse>('/auth/forgot-password', { email }))
        .data,
    resetPassword: async (token, newPassword) => {
      await client.post('/auth/reset-password', { token, newPassword })
    },
    refresh: async () =>
      (await client.post<AccessTokenResponse>('/auth/refresh')).data,
  }
}
