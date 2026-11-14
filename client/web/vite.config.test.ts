import type { ConfigEnv, UserConfigFn } from 'vite'
import { expect, it } from 'vitest'
import config from './vite.config'

it('uses the fixed local development port', async () => {
  const createConfig = config as UserConfigFn
  const env: ConfigEnv = {
    command: 'serve',
    mode: 'development',
    isSsrBuild: false,
    isPreview: false,
  }
  const resolved = await createConfig(env)

  expect(resolved.server).toMatchObject({ port: 3000, strictPort: true })
})
