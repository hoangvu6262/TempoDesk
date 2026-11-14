import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'
afterEach(() => {
  cleanup()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  vi.useRealTimers()
})
