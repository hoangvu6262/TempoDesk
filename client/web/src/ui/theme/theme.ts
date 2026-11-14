import { THEME_STORAGE_KEY } from '../../constants/storage'

export type ThemePreference = 'light' | 'dark' | 'system'
export type ResolvedTheme = Exclude<ThemePreference, 'system'>

export function resolveTheme(
  preference: ThemePreference,
  systemIsDark: boolean,
): ResolvedTheme {
  return preference === 'system'
    ? systemIsDark
      ? 'dark'
      : 'light'
    : preference
}

export function readThemePreference(
  storage: Pick<Storage, 'getItem'> = localStorage,
): ThemePreference {
  const value = storage.getItem(THEME_STORAGE_KEY)
  return value === 'light' || value === 'dark' || value === 'system'
    ? value
    : 'system'
}

export function applyInitialTheme() {
  const preference = readThemePreference()
  const systemIsDark = matchMedia('(prefers-color-scheme: dark)').matches
  document.documentElement.dataset.theme = resolveTheme(
    preference,
    systemIsDark,
  )
}
