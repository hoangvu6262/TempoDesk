import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type PropsWithChildren,
} from 'react'
import { useApiRuntime } from '../../../lib/api'
import {
  createAuthenticationApi,
  type AccessTokenResponse,
  type AuthenticationApi,
} from '../../../api/auth-api'
import { AuthContext, type AuthStatus } from './AuthContext'

const REFRESH_EARLY_MS = 30_000
const MIN_REFRESH_DELAY_MS = 1_000

type AuthProviderProps = PropsWithChildren<{ api?: AuthenticationApi }>

export function AuthProvider({ api, children }: AuthProviderProps) {
  const { client, setAccessToken, setUnauthorizedHandler } = useApiRuntime()
  const timerRef = useRef<number | undefined>(undefined)
  const refreshPromiseRef = useRef<Promise<void> | null>(null)
  const refreshRef = useRef<() => Promise<void>>(async () => {})
  const initializedRef = useRef(false)
  const activeRef = useRef(true)
  const sessionGenerationRef = useRef(0)
  const [status, setStatus] = useState<AuthStatus>('initializing')
  const [authApi] = useState(() => api ?? createAuthenticationApi(client))

  const clearSession = useCallback(() => {
    sessionGenerationRef.current += 1
    setAccessToken(null)
    window.clearTimeout(timerRef.current)
    timerRef.current = undefined
    if (activeRef.current) setStatus('anonymous')
  }, [setAccessToken])

  const applyToken = useCallback(
    (result: AccessTokenResponse) => {
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

  const runRefresh = useCallback(() => {
    if (refreshPromiseRef.current) return refreshPromiseRef.current
    const generation = sessionGenerationRef.current
    const request = authApi
      .refresh()
      .then((result) => {
        if (sessionGenerationRef.current === generation) applyToken(result)
      })
      .catch(() => {
        if (sessionGenerationRef.current === generation) clearSession()
      })
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
    setUnauthorizedHandler(clearSession)
    return () => setUnauthorizedHandler(null)
  }, [clearSession, setUnauthorizedHandler])

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

  const value = useMemo(
    () => ({ status, api: authApi, login, logoutLocal: clearSession }),
    [authApi, clearSession, login, status],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}
