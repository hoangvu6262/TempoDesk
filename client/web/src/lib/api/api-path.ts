import { ApiError } from './api-error'

export function assertSafeApiPath(path: string) {
  if (
    !path.startsWith('/') ||
    path.startsWith('//') ||
    /[\s\p{Cc}\\?#%]/u.test(path) ||
    path.split('/').some((part) => part === '..' || part === '.')
  ) {
    throw new ApiError('Đường dẫn API không hợp lệ.', {
      kind: 'invalid-response',
    })
  }
}
