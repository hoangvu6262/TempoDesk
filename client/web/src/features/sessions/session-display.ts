const BROWSERS: Array<[RegExp, string]> = [
  [/Edg\//, 'Microsoft Edge'],
  [/OPR\//, 'Opera'],
  [/Chrome\//, 'Chrome'],
  [/Firefox\//, 'Firefox'],
  [/Safari\//, 'Safari'],
]

const PLATFORMS: Array<[RegExp, string]> = [
  [/iPhone/, 'iPhone'],
  [/iPad/, 'iPad'],
  [/Android/, 'Android'],
  [/Windows/, 'Windows'],
  [/Macintosh|Mac OS X/, 'macOS'],
  [/Linux/, 'Linux'],
]

export function describeDevice(userAgent: string | null) {
  if (!userAgent) return 'Unknown device'
  const browser = BROWSERS.find(([pattern]) => pattern.test(userAgent))?.[1]
  const platform = PLATFORMS.find(([pattern]) => pattern.test(userAgent))?.[1]
  if (browser && platform) return `${browser} on ${platform}`
  return browser ?? platform ?? 'Unknown device'
}

export function formatSessionDate(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? 'Unknown'
    : new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short',
      }).format(date)
}
