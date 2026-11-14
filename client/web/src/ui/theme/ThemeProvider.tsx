import { type PropsWithChildren, useEffect, useMemo, useState } from 'react'
import { THEME_STORAGE_KEY } from '../../constants/storage'
import { readThemePreference, resolveTheme } from './theme'
import { ThemeContext, type ThemeContextValue } from './useTheme'

export function ThemeProvider({ children }: PropsWithChildren) {
  const [media] = useState(() => matchMedia('(prefers-color-scheme: dark)'))
  const [preference, setStoredPreference] = useState(readThemePreference)
  const [systemIsDark, setSystemIsDark] = useState(media.matches)
  const resolvedTheme = resolveTheme(preference, systemIsDark)

  useEffect(() => {
    if (preference !== 'system') return
    const onChange = (event: MediaQueryListEvent) =>
      setSystemIsDark(event.matches)
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [media, preference])

  useEffect(() => {
    document.documentElement.dataset.theme = resolvedTheme
  }, [resolvedTheme])

  const value = useMemo<ThemeContextValue>(
    () => ({
      preference,
      resolvedTheme,
      setPreference(nextPreference) {
        localStorage.setItem(THEME_STORAGE_KEY, nextPreference)
        setStoredPreference(nextPreference)
      },
    }),
    [preference, resolvedTheme],
  )

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}
