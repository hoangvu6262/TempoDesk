import { createContext, useContext } from 'react'
import type { AuthenticationApi } from '../../../api/auth-api'

export type AuthStatus = 'initializing' | 'authenticated' | 'anonymous'

export type AuthContextValue = {
  status: AuthStatus
  api: AuthenticationApi
  login(email: string, password: string): Promise<void>
  logoutLocal(): void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth() {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used within AuthProvider')
  return value
}
