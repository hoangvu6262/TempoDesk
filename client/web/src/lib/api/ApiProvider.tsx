import { useState, type PropsWithChildren } from 'react'
import { readClientConfig } from '../../config/env'
import { ApiContext, type ApiRuntime } from './ApiContext'
import { createApiClient } from './api-client'

function createApiRuntime(): ApiRuntime {
  let accessToken: string | null = null
  let unauthorizedHandler: (() => void) | null = null
  const client = createApiClient(
    readClientConfig(import.meta.env).apiBaseUrl,
    () =>
      accessToken
        ? new Headers({ Authorization: `Bearer ${accessToken}` })
        : new Headers(),
    () => unauthorizedHandler?.(),
  )
  client.defaults.withCredentials = true

  return {
    client,
    setAccessToken: (value) => {
      accessToken = value
    },
    setUnauthorizedHandler: (handler) => {
      unauthorizedHandler = handler
    },
  }
}

export function ApiProvider({ children }: PropsWithChildren) {
  const [runtime] = useState(createApiRuntime)
  return <ApiContext value={runtime}>{children}</ApiContext>
}
