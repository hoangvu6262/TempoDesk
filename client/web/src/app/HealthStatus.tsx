import { useEffect, useState } from 'react'
import { ApiError, createApiClient } from '../lib/api-client'
type HealthState = 'loading' | 'healthy' | 'unavailable' | 'malformed'
export function HealthStatus({ baseUrl }: { baseUrl: string }) {
  const [state, setState] = useState<HealthState>('loading')
  useEffect(() => {
    const controller = new AbortController()
    createApiClient(baseUrl)
      .get<unknown>('/health/ready', { signal: controller.signal })
      .then(({ data }) => {
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
    loading: 'Checking API…',
    healthy: 'API is ready',
    unavailable: 'API is unavailable',
    malformed: 'The health response is invalid',
  }
  return (
    <div
      className={`td-health-status td-health-status--${state}`}
      role="status"
      aria-live="polite"
    >
      <span className="td-health-status__dot" aria-hidden="true" />
      <div>
        <strong>{labels[state]}</strong>
        <p>Check the API connection and database status.</p>
      </div>
    </div>
  )
}
