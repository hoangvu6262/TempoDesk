import { ApiError } from '../../../lib/api'

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function validateEmail(value: string) {
  if (!value) return 'Email is required.'
  if (value.length > 256) return 'Email must be 256 characters or fewer.'
  if (!EMAIL_PATTERN.test(value)) return 'Enter a valid email address.'
}

export function validatePassword(value: string, minimum = 12) {
  if (!value) return 'Password is required.'
  if (value.length < minimum)
    return `Password must be at least ${minimum} characters.`
  if (value.length > 1024)
    return 'Password must be 1024 characters or fewer.'
}

export function validateConfirmation(
  password: string,
  confirmation: string,
) {
  if (!confirmation) return 'Confirm your password.'
  if (password !== confirmation) return 'Passwords do not match.'
}

export function fieldError(error: unknown, name: string) {
  if (!(error instanceof ApiError) || !error.errors) return undefined
  const key = Object.keys(error.errors).find(
    (candidate) => candidate.toLowerCase() === name.toLowerCase(),
  )
  return key ? error.errors[key][0] : undefined
}

export function pageError(error: unknown, fieldNames: string[]) {
  if (!(error instanceof ApiError)) return 'Unable to complete the request.'
  const known = new Set(fieldNames.map((name) => name.toLowerCase()))
  const unmatched = Object.entries(error.errors ?? {})
    .filter(([name]) => !known.has(name.toLowerCase()))
    .flatMap(([, messages]) => messages)
  return (
    unmatched[0] ??
    (Object.keys(error.errors ?? {}).length ? undefined : error.detail)
  )
}
