import { useEffect, useState } from 'react'
import { ApiError, createApiClient } from '../lib/api-client'
type HealthState = 'loading' | 'healthy' | 'unavailable' | 'malformed'
export function HealthStatus({ baseUrl }: { baseUrl: string }) {
  const [state, setState] = useState<HealthState>('loading')
  useEffect(() => {
    const controller = new AbortController()
    createApiClient(baseUrl)<unknown>('/health/ready', {
      signal: controller.signal,
    })
      .then((data) => {
        if (controller.signal.aborted) return
        if (
          typeof data !== 'object' ||
          data === null ||
          !('status' in data) ||
          !('elapsedMs' in data) ||
          (data.status !== 'Healthy' && data.status !== 'Unhealthy') ||
          typeof data.elapsedMs !== 'number' ||
          !Number.isFinite(data.elapsedMs) ||
          data.elapsedMs < 0
        )
          setState('malformed')
        else setState(data.status === 'Healthy' ? 'healthy' : 'unavailable')
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted)
          setState(
            error instanceof ApiError && error.kind === 'invalid-response'
              ? 'malformed'
              : 'unavailable',
          )
      })
    return () => controller.abort()
  }, [baseUrl])
  const labels = {
    loading: 'Đang kiểm tra API…',
    healthy: 'API sẵn sàng',
    unavailable: 'API chưa sẵn sàng',
    malformed: 'Phản hồi sức khỏe không hợp lệ',
  }
  return (
    <div className={`health ${state}`} role="status" aria-live="polite">
      <span className="status-dot" aria-hidden="true" />
      <div>
        <strong>{labels[state]}</strong>
        <p>Kiểm tra kết nối và trạng thái cơ sở dữ liệu.</p>
      </div>
    </div>
  )
}
