import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type PropsWithChildren,
} from 'react'
import { ApiError, useApiRuntime } from '../../../lib/api'
import {
  createAuthenticationApi,
  type AccessTokenResponse,
  type AuthenticationApi,
} from '../../../api/auth-api'
import { AuthContext, type AuthStatus } from './AuthContext'

const REFRESH_EARLY_MS = 30_000
const MIN_REFRESH_DELAY_MS = 1_000
const REFRESH_RETRY_BASE_MS = 5_000
const REFRESH_RETRY_MAX_MS = 60_000

type AuthProviderProps = PropsWithChildren<{ api?: AuthenticationApi }>

export function AuthProvider({ api, children }: AuthProviderProps) {
  const { client, setAccessToken, setUnauthorizedHandler } = useApiRuntime()
  const timerRef = useRef<number | undefined>(undefined)
  const refreshPromiseRef = useRef<Promise<boolean> | null>(null)
  const refreshRef = useRef<() => Promise<boolean>>(async () => false)
  const hasAccessTokenRef = useRef(false)
  const refreshFailureCountRef = useRef(0)
  const initializedRef = useRef(false)
  const activeRef = useRef(true)
  const sessionGenerationRef = useRef(0)
  const [status, setStatus] = useState<AuthStatus>('initializing')
  const [authApi] = useState(() => api ?? createAuthenticationApi(client))

  const clearSession = useCallback(() => {
    sessionGenerationRef.current += 1
    hasAccessTokenRef.current = false
    refreshFailureCountRef.current = 0
    setAccessToken(null)
    window.clearTimeout(timerRef.current)
    timerRef.current = undefined
    if (activeRef.current) setStatus('anonymous')
  }, [setAccessToken])

  const applyToken = useCallback(
    (result: AccessTokenResponse) => {
      hasAccessTokenRef.current = true
      refreshFailureCountRef.current = 0
      setAccessToken(result.accessToken)
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
    [setAccessToken],
  )

  const scheduleRefreshRetry = useCallback(() => {
    const failureCount = refreshFailureCountRef.current + 1
    refreshFailureCountRef.current = failureCount
    const delay = Math.min(
      REFRESH_RETRY_BASE_MS * 2 ** (failureCount - 1),
      REFRESH_RETRY_MAX_MS,
    )
    window.clearTimeout(timerRef.current)
    timerRef.current = window.setTimeout(() => void refreshRef.current(), delay)
  }, [])

  const runRefresh = useCallback(() => {
    if (refreshPromiseRef.current) return refreshPromiseRef.current
    const generation = sessionGenerationRef.current
    const request = authApi
      .refresh()
      .then((result) => {
        if (!activeRef.current || sessionGenerationRef.current !== generation) {
          return false
        }
        applyToken(result)
        return true
      })
      .catch((error: unknown) => {
        if (!activeRef.current || sessionGenerationRef.current !== generation) {
          return false
        }
        if (error instanceof ApiError && error.status === 401) {
          clearSession()
          return false
        }
        if (!hasAccessTokenRef.current) setStatus('unavailable')
        scheduleRefreshRetry()
        return false
      })
      .finally(() => {
        refreshPromiseRef.current = null
      })
    refreshPromiseRef.current = request
    return request
  }, [applyToken, authApi, clearSession, scheduleRefreshRetry])

  useEffect(() => {
    refreshRef.current = runRefresh
  }, [runRefresh])

  useEffect(() => {
    setUnauthorizedHandler(runRefresh)
    return () => setUnauthorizedHandler(null)
  }, [runRefresh, setUnauthorizedHandler])

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
      const result = await authApi.login(email, password)
      sessionGenerationRef.current += 1
      applyToken(result)
    },
    [applyToken, authApi],
  )

  const retrySession = useCallback(async () => {
    if (activeRef.current) setStatus('initializing')
    await runRefresh()
  }, [runRefresh])

  const value = useMemo(
    () => ({
      status,
      api: authApi,
      login,
      retrySession,
      logoutLocal: clearSession,
    }),
    [authApi, clearSession, login, retrySession, status],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}
