# Axios API Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the custom Fetch transport with a focused Axios API boundary while preserving TempoDesk's existing security and error behavior.

**Architecture:** A configured Axios instance owns transport, timeout, cancellation, and interceptors. Small modules own safe path validation and `ApiError` normalization; the existing `src/lib/api-client.ts` becomes a compatibility facade, and cross-module policy values move to `src/constants`.

**Tech Stack:** React 19, TypeScript 6, Axios, Vitest 5, Testing Library.

## Global Constraints

- Add Axios as the only new runtime dependency; do not add TanStack Query.
- Restore `typescript-eslint` to `^8.69.0`; do not retain the uncommitted `^7.2.0` downgrade.
- Preserve path validation before dynamic headers or network work.
- Preserve `ApiError` kinds, safe Vietnamese messages, Problem Details fields, timeout, cancellation, and network normalization.
- Use `src/constants` only for stable shared policy values; keep feature paths, query keys, and one-off copy local.
- Do not add authentication refresh/retry interceptors.

---

### Task 1: Dependency and shared constants

**Files:**

- Modify: `client/web/package.json`
- Modify: `client/web/package-lock.json`
- Create: `client/web/src/constants/http.ts`
- Create: `client/web/src/constants/storage.ts`
- Modify: `client/web/src/ui/theme/theme.ts`
- Modify: `client/web/src/ui/theme/ThemeProvider.tsx`

**Interfaces:**

- Produces: `DEFAULT_API_TIMEOUT_MS = 10_000`.
- Produces: `JSON_MEDIA_TYPE`, `PROBLEM_JSON_MEDIA_TYPE`, and `JSON_ACCEPT_HEADER`.
- Produces: `THEME_STORAGE_KEY = 'tempodesk.theme'`.

- [ ] **Step 1: Restore the approved lint toolchain and install Axios**

Set `typescript-eslint` back to `^8.69.0` in `client/web/package.json`, then run:

```bash
source ~/.nvm/nvm.sh
nvm use
npm --prefix client/web install axios
```

Expected: Axios appears under runtime dependencies; `typescript-eslint` remains `^8.69.0`; the lockfile no longer contains the forced v7 downgrade.

- [ ] **Step 2: Add exact shared constants**

Create `client/web/src/constants/http.ts`:

```ts
export const DEFAULT_API_TIMEOUT_MS = 10_000
export const JSON_MEDIA_TYPE = 'application/json'
export const PROBLEM_JSON_MEDIA_TYPE = 'application/problem+json'
export const JSON_ACCEPT_HEADER = `${JSON_MEDIA_TYPE}, ${PROBLEM_JSON_MEDIA_TYPE}`
```

Create `client/web/src/constants/storage.ts`:

```ts
export const THEME_STORAGE_KEY = 'tempodesk.theme'
```

- [ ] **Step 3: Consume the storage constant without changing behavior**

In `theme.ts`, remove the local declaration and import:

```ts
import { THEME_STORAGE_KEY } from '../../constants/storage'
```

In `ThemeProvider.tsx`, import `THEME_STORAGE_KEY` from `../../constants/storage` and keep `readThemePreference` and `resolveTheme` imported from `./theme`.

- [ ] **Step 4: Verify and commit**

Run:

```bash
npm --prefix client/web run test -- src/ui/theme/ThemeProvider.test.tsx
npm --prefix client/web run lint
```

Expected: two theme tests pass and lint exits `0`.

Commit:

```bash
git add client/web/package.json client/web/package-lock.json client/web/src/constants client/web/src/ui/theme
git commit -m "chore(frontend): add Axios and shared constants"
```

---

### Task 2: Axios API boundary and caller migration

**Files:**

- Create: `client/web/src/lib/api/api-error.ts`
- Create: `client/web/src/lib/api/api-path.ts`
- Create: `client/web/src/lib/api/api-client.ts`
- Create: `client/web/src/lib/api/index.ts`
- Modify: `client/web/src/lib/api-client.ts`
- Modify: `client/web/src/lib/api-client.test.ts`
- Modify: `client/web/src/app/HealthStatus.tsx`
- Verify: `client/web/src/app/HealthStatus.test.tsx`

**Interfaces:**

- Produces: `ApiError` with existing `status`, `detail`, `traceId`, `errors`, and `kind` fields.
- Produces: `assertSafeApiPath(path: string): void`.
- Produces: `toApiError(error: unknown): ApiError`.
- Produces: `createApiClient(baseUrl: string, requestHeaders?: () => HeadersInit): AxiosInstance`.
- Consumes: callers use `api.get<T>(path, config)` and read `response.data`.

- [ ] **Step 1: Rewrite the API boundary tests for the Axios contract**

Keep every existing behavior case, but replace Fetch stubs with an Axios adapter assigned through `request.defaults.adapter`. The success test must call:

```ts
const response = await request.post<{ id: number }>('/tasks', { title: 'Test' })
expect(response.data).toEqual({ id: 1 })
```

Its adapter assertion must prove:

```ts
expect(config.baseURL).toBe('/api')
expect(config.url).toBe('/tasks')
expect(config.headers.get('Authorization')).toBe('Bearer test-token')
expect(config.headers.get('Content-Type')).toContain('application/json')
expect(config.data).toBe('{"title":"Test"}')
```

For HTTP failures, reject the adapter with an `AxiosError` containing an `AxiosResponse`. Retain assertions for safe Problem Details, hidden non-problem text, malformed successful JSON, unsafe paths before headers/adapter, timeout, caller cancellation, pre-cancelled signals, and network errors.

- [ ] **Step 2: Run the tests to verify RED**

Run:

```bash
npm --prefix client/web run test -- src/lib/api-client.test.ts
```

Expected: FAIL because the current `createApiClient` returns a request function without `.get`, `.post`, or `.defaults.adapter`.

- [ ] **Step 3: Extract safe path validation**

Create `api-path.ts`:

```ts
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
```

- [ ] **Step 4: Extract safe API error normalization**

Create `api-error.ts` with the existing `ApiError` class. Add `toApiError(error)` using these exact precedence rules:

```text
existing ApiError -> unchanged
Axios ERR_CANCELED -> cancelled
Axios ECONNABORTED or ETIMEDOUT -> timeout
Axios 2xx response with ERR_BAD_RESPONSE -> invalid-response
Axios response -> http, with fields only from valid application/problem+json
Axios error without response -> network
unknown error -> network
```

Validate `problem.errors` as `Record<string, string[]>`; ignore malformed fields. Use the existing Vietnamese messages unchanged.

- [ ] **Step 5: Implement the Axios client**

Create `api-client.ts` using:

```ts
const api = axios.create({
  baseURL: baseUrl.replace(/\/$/, ''),
  timeout: DEFAULT_API_TIMEOUT_MS,
  responseType: 'json',
  transitional: { silentJSONParsing: false },
})
```

The request interceptor calls `assertSafeApiPath(config.url ?? '')`, validates `config.timeout` as finite and positive, merges `requestHeaders()` into `config.headers`, and sets `Accept` to `JSON_ACCEPT_HEADER`. The response error interceptor rejects with `toApiError(error)`. Return the configured `AxiosInstance`.

Create `api/index.ts`:

```ts
export { createApiClient } from './api-client'
export { ApiError } from './api-error'
```

Replace the legacy `src/lib/api-client.ts` body with:

```ts
export { ApiError, createApiClient } from './api'
```

- [ ] **Step 6: Migrate HealthStatus**

Create the client once per `baseUrl` inside the effect, then replace the request call with:

```ts
createApiClient(baseUrl)
  .get<unknown>('/health/ready', { signal: controller.signal })
  .then(({ data }) => {
    // retain the existing untrusted response validation and state mapping
  })
```

Keep abort cleanup and all existing health states unchanged.

- [ ] **Step 7: Verify GREEN and full frontend quality gates**

Run:

```bash
npm --prefix client/web run format
npm --prefix client/web run lint
npm --prefix client/web run test -- --coverage
npm --prefix client/web run build
npm --prefix client/web audit --audit-level=high
```

Expected: format and lint exit `0`; all tests pass with coverage; build exits `0`; audit reports no high/critical vulnerability.

- [ ] **Step 8: Commit**

```bash
git add client/web/src/lib client/web/src/app/HealthStatus.tsx client/web/src/app/HealthStatus.test.tsx
git commit -m "refactor(frontend): use Axios for API transport"
```
