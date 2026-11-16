import { ApiError } from '../../lib/api'

export function workspaceFieldError(error: unknown, fieldName: string) {
  if (!(error instanceof ApiError) || !error.errors) return undefined
  const key = Object.keys(error.errors).find(
    (candidate) => candidate.toLowerCase() === fieldName.toLowerCase(),
  )
  return key ? error.errors[key][0] : undefined
}

export function workspaceRequestError(error: unknown, fallback: string) {
  return error instanceof ApiError && error.detail ? error.detail : fallback
}
