import { render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { HealthStatus } from './HealthStatus'
it('shows loading while awaiting API readiness', () => {
  vi.stubGlobal(
    'fetch',
    vi.fn(() => new Promise(() => {})),
  )
  render(<HealthStatus baseUrl="/api" />)
  expect(screen.getByRole('status')).toHaveTextContent('Đang kiểm tra API')
})
it('shows healthy readiness', async () => {
  vi.stubGlobal(
    'fetch',
    vi
      .fn()
      .mockResolvedValue(Response.json({ status: 'Healthy', elapsedMs: 1.5 })),
  )
  render(<HealthStatus baseUrl="/api" />)
  expect(await screen.findByText('API sẵn sàng')).toBeVisible()
})
it('shows malformed readiness when JSON cannot be parsed', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('not-json')))
  render(<HealthStatus baseUrl="/api" />)
  expect(
    await screen.findByText('Phản hồi sức khỏe không hợp lệ'),
  ).toBeVisible()
})
it.each([
  new Response(null, { status: 503 }),
  Response.json({ status: 'Unhealthy', elapsedMs: 1 }),
])('shows unavailable readiness', async (response) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))
  render(<HealthStatus baseUrl="/api" />)
  expect(await screen.findByText('API chưa sẵn sàng')).toBeVisible()
})
it.each([
  {},
  { status: 'Healthy' },
  { status: 'Unknown', elapsedMs: 1 },
  { status: ['Healthy'], elapsedMs: 1 },
  { status: 'Healthy', elapsedMs: -1 },
])('rejects malformed health data %j', async (body) => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(Response.json(body)))
  render(<HealthStatus baseUrl="/api" />)
  expect(
    await screen.findByText('Phản hồi sức khỏe không hợp lệ'),
  ).toBeVisible()
})
