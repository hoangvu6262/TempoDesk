const AUTHENTICATION_PATHS = new Set([
  '/login',
  '/register',
  '/forgot-password',
  '/reset-password',
  '/verify-email',
  '/auth/google/callback',
])

export function safeReturnPath(
  value: string | null | undefined,
  fallback = '/app',
) {
  if (
    !value ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    value.includes('\\')
  )
    return fallback

  let url: URL
  try {
    url = new URL(value, window.location.origin)
  } catch {
    return fallback
  }

  return url.origin === window.location.origin &&
    !AUTHENTICATION_PATHS.has(url.pathname)
    ? `${url.pathname}${url.search}${url.hash}`
    : fallback
}
