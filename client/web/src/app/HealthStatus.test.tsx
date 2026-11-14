import { render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { ApiError } from '../lib/api-client'
import { HealthStatus } from './HealthStatus'

const api = vi.hoisted(() => ({ get: vi.fn() }))

vi.mock('../lib/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../lib/api-client')>()),
  createApiClient: () => api,
}))

beforeEach(() => {
  api.get.mockReset()
})

it('shows loading while awaiting API readiness', () => {
  api.get.mockImplementation(
    (_path: string, config: { signal: AbortSignal }) =>
      new Promise((_resolve, reject) => {
        config.signal.addEventListener(
          'abort',
          () =>
            reject(
              new ApiError('The request was cancelled.', {
                kind: 'cancelled',
              }),
            ),
          { once: true },
        )
      }),
  )
  render(<HealthStatus baseUrl="/api" />)
  expect(screen.getByRole('status')).toHaveTextContent('Checking API')
})

it('shows healthy readiness', async () => {
  api.get.mockResolvedValue({
    data: { status: 'Healthy', elapsedMs: 1.5 },
  })
  render(<HealthStatus baseUrl="/api" />)
  expect(await screen.findByText('API is ready')).toBeVisible()
})

it('shows malformed readiness when JSON cannot be parsed', async () => {
  api.get.mockRejectedValue(
    new ApiError('The API response is invalid.', {
      kind: 'invalid-response',
    }),
  )
  render(<HealthStatus baseUrl="/api" />)
  expect(
    await screen.findByText('The health response is invalid'),
  ).toBeVisible()
})

it('shows unavailable readiness for an API failure', async () => {
  api.get.mockRejectedValue(
    new ApiError('Service unavailable', { status: 503 }),
  )
  render(<HealthStatus baseUrl="/api" />)
  expect(await screen.findByText('API is unavailable')).toBeVisible()
})

it('shows unavailable readiness for an unhealthy response', async () => {
  api.get.mockResolvedValue({
    data: { status: 'Unhealthy', elapsedMs: 1 },
  })
  render(<HealthStatus baseUrl="/api" />)
  expect(await screen.findByText('API is unavailable')).toBeVisible()
})

it.each([
  {},
  { status: 'Healthy' },
  { status: 'Unknown', elapsedMs: 1 },
  { status: ['Healthy'], elapsedMs: 1 },
  { status: 'Healthy', elapsedMs: -1 },
])('rejects malformed health data %j', async (data) => {
  api.get.mockResolvedValue({ data })
  render(<HealthStatus baseUrl="/api" />)
  expect(
    await screen.findByText('The health response is invalid'),
  ).toBeVisible()
})
