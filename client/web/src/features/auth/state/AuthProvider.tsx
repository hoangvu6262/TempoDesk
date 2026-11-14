import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type PropsWithChildren,
} from 'react'
import { readClientConfig } from '../../../config/env'
import { createApiClient } from '../../../lib/api'
import {
  createAuthenticationApi,
  type AccessTokenResponse,
  type AuthenticationApi,
} from '../api/auth-api'
import { AuthContext, type AuthStatus } from './AuthContext'

const REFRESH_EARLY_MS = 30_000
const MIN_REFRESH_DELAY_MS = 1_000

type AuthProviderProps = PropsWithChildren<{ api?: AuthenticationApi }>

function createAuthRuntime(injectedApi?: AuthenticationApi) {
  if (injectedApi) {
    return {
      api: injectedApi,
      setAccessToken: (value: string | null) => void value,
    }
  }
  let accessToken: string | null = null
  const client = createApiClient(
    readClientConfig(import.meta.env).apiBaseUrl,
    () =>
      accessToken
        ? new Headers({ Authorization: `Bearer ${accessToken}` })
        : new Headers(),
  )
  client.defaults.withCredentials = true
  return {
    api: createAuthenticationApi(client),
    setAccessToken: (value: string | null) => {
      accessToken = value
    },
  }
}

export function AuthProvider({ api, children }: AuthProviderProps) {
  const timerRef = useRef<number | undefined>(undefined)
  const refreshPromiseRef = useRef<Promise<void> | null>(null)
  const refreshRef = useRef<() => Promise<void>>(async () => {})
  const initializedRef = useRef(false)
  const activeRef = useRef(true)
  const [status, setStatus] = useState<AuthStatus>('initializing')
  const [runtime] = useState(() => createAuthRuntime(api))
  const authApi = runtime.api

  const clearSession = useCallback(() => {
    runtime.setAccessToken(null)
    window.clearTimeout(timerRef.current)
    timerRef.current = undefined
    if (activeRef.current) setStatus('anonymous')
  }, [runtime])

  const applyToken = useCallback(
    (result: AccessTokenResponse) => {
      runtime.setAccessToken(result.accessToken)
      if (activeRef.current) setStatus('authenticated')
      window.clearTimeout(timerRef.current)
      const delay = Math.max(
        MIN_REFRESH_DELAY_MS,
        result.expiresIn * 1_000 - REFRESH_EARLY_MS,
      )
      timerRef.current = window.setTimeout(
        () => void refreshRef.current(),
        delay,
      )
    },
    [runtime],
  )

  const runRefresh = useCallback(() => {
    if (refreshPromiseRef.current) return refreshPromiseRef.current
    const request = authApi
      .refresh()
      .then(applyToken)
      .catch(clearSession)
      .finally(() => {
        refreshPromiseRef.current = null
      })
    refreshPromiseRef.current = request
    return request
  }, [applyToken, authApi, clearSession])

  useEffect(() => {
    refreshRef.current = runRefresh
  }, [runRefresh])

  useEffect(() => {
    activeRef.current = true
    if (!initializedRef.current) {
      initializedRef.current = true
      void runRefresh()
    }
    return () => {
      activeRef.current = false
      window.clearTimeout(timerRef.current)
    }
  }, [runRefresh])

  const login = useCallback(
    async (email: string, password: string) => {
      applyToken(await authApi.login(email, password))
    },
    [applyToken, authApi],
  )

  const value = useMemo(
    () => ({ status, api: authApi, login, logoutLocal: clearSession }),
    [authApi, clearSession, login, status],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}
