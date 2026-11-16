export interface ClientConfig {
  apiBaseUrl: string
  signalRBaseUrl: string
}

function baseUrl(
  value: string | undefined,
  fallback: string,
  name: string,
): string {
  const input = value ?? fallback
  const invalid = () =>
    new Error(
      `${name} must be an HTTP(S) URL or a path beginning with one slash.`,
    )
  if (!input || /[\s\\?#%]/.test(input)) throw invalid()
  if (input.startsWith('/') && !input.startsWith('//')) {
    if (input.split('/').some((part) => part === '.' || part === '..'))
      throw invalid()
    return input.replace(/\/+$/, '') || '/'
  }
  let url: URL
  if (!/^https?:\/\//.test(input)) throw invalid()
  try {
    url = new URL(input)
  } catch {
    throw invalid()
  }
  if (
    !['http:', 'https:'].includes(url.protocol) ||
    url.username ||
    url.password
  )
    throw invalid()
  return url.href.replace(/\/+$/, '')
}

export function readClientConfig(env: {
  VITE_API_BASE_URL?: string
  VITE_SIGNALR_BASE_URL?: string
}): ClientConfig {
  return {
    apiBaseUrl: baseUrl(env.VITE_API_BASE_URL, '/', 'VITE_API_BASE_URL'),
    signalRBaseUrl: baseUrl(
      env.VITE_SIGNALR_BASE_URL,
      '/hubs',
      'VITE_SIGNALR_BASE_URL',
    ),
  }
}
