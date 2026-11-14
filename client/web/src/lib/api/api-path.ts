import { API_ERROR_MESSAGE } from '../../constants/errors'
import { ApiError } from './api-error'

export function assertSafeApiPath(path: string) {
  if (
    !path.startsWith('/') ||
    path.startsWith('//') ||
    /[\s\p{Cc}\\?#%]/u.test(path) ||
    path.split('/').some((part) => part === '..' || part === '.')
  ) {
    throw new ApiError(API_ERROR_MESSAGE.invalidPath, {
      kind: 'invalid-response',
    })
  }
}
