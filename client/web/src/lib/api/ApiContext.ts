import type { AxiosInstance } from 'axios'
import { createContext, useContext } from 'react'

export type ApiRuntime = {
  client: AxiosInstance
  setAccessToken(value: string | null): void
  setUnauthorizedHandler(handler: (() => void) | null): void
}

export const ApiContext = createContext<ApiRuntime | null>(null)

export function useApiRuntime() {
  const value = useContext(ApiContext)
  if (!value) throw new Error('useApiRuntime must be used within ApiProvider')
  return value
}
