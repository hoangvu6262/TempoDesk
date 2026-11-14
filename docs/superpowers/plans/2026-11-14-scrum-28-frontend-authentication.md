# SCRUM-28 Frontend Authentication Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the React registration, verification, login, password recovery, silent-refresh, and protected-routing experience against the existing TempoDesk authentication API.

**Architecture:** A feature-local API adapter wraps the existing Axios boundary. `AuthProvider` owns the access token only in memory, initializes and refreshes through the HttpOnly refresh cookie, and exposes auth operations to route-level pages. `ProtectedRoute` waits for initialization and validates same-origin return paths before redirecting.

**Tech Stack:** React 19, TypeScript 6, React Router 8, Axios, Vitest 5, Testing Library, Sass, existing TempoDesk UI kit.

## Global Constraints

- Keep `/` public and protect `/app`.
- Use only the existing dependencies and UI kit; add no form, state, or component package.
- Keep access tokens in provider memory only. Never write access or refresh tokens to local storage, session storage, URLs, logs, or rendered output.
- The refresh token remains in the server-managed HttpOnly cookie.
- Google OAuth backend work is out of scope. Render a disabled Google action and an inert callback page.
- API paths are relative to `VITE_API_BASE_URL`, whose default is `/api`.
- Email is required, valid, and at most 256 characters. Passwords are at most 1024 characters; registration/reset require at least 12 characters and matching confirmation. Verification/reset tokens are at most 512 characters.
- Every behavior change follows red-green-refactor. Run the focused failing test before production code, then the focused passing test.
- Do not modify server code.

---

## File map

Create the following focused units:

- `client/web/src/features/auth/api/auth-api.ts`: auth request/response types and API adapter.
- `client/web/src/features/auth/forms/form-errors.ts`: client validation and Problem Details field mapping.
- `client/web/src/features/auth/state/AuthContext.ts`: public auth state contract and hook.
- `client/web/src/features/auth/state/AuthProvider.tsx`: in-memory token and refresh lifecycle.
- `client/web/src/features/auth/routing/auth-return-path.ts`: return-path allow-list.
- `client/web/src/features/auth/routing/ProtectedRoute.tsx`: initialization gate and anonymous redirect.
- `client/web/src/features/auth/pages/*.tsx`: route-level auth screens.
- `client/web/src/features/auth/pages/AuthPages.scss`: layout shared only by auth pages.
- `client/web/src/app/AuthenticatedHome.tsx`: minimal private destination.

Modify:

- `client/web/src/App.tsx`: compose public auth routes and `/app` protection.
- `client/web/src/main.tsx`: mount `AuthProvider` inside `BrowserRouter`.
- `client/web/vite.config.ts`: include `src/features/**` in coverage.

Keep tests beside each unit as `*.test.ts` or `*.test.tsx`.

---

### Task 1: Authentication API adapter

**Files:**
- Create: `client/web/src/features/auth/api/auth-api.ts`
- Create: `client/web/src/features/auth/api/auth-api.test.ts`

**Interfaces:**
- Consumes: `AxiosInstance` returned by `createApiClient`.
- Produces: `AccessTokenResponse`, `AcceptedResponse`, `AuthenticationApi`, and `createAuthenticationApi(client)`.

- [ ] **Step 1: Write the failing adapter tests**

Create tests with an Axios adapter that records requests and returns JSON/no-content responses:

```ts
it('uses the existing authentication contracts', async () => {
  const calls: Array<{ method?: string; url?: string; data?: string }> = []
  const client = createApiClient('/api')
  client.defaults.adapter = async (config) => {
    calls.push({ method: config.method, url: config.url, data: config.data })
    const data = config.url === '/auth/login'
      ? { accessToken: 'access', tokenType: 'Bearer', expiresIn: 300 }
      : config.url?.includes('register') ||
          config.url?.includes('resend-verification') ||
          config.url?.includes('forgot')
        ? { message: 'Accepted' }
        : undefined
    return { config, data, headers: {}, status: data ? 200 : 204, statusText: '' }
  }
  const api = createAuthenticationApi(client)

  await api.register('person@example.com', 'LongPassword1')
  await api.verifyEmail('verification-token')
  await api.resendVerification('person@example.com')
  await api.login('person@example.com', 'LongPassword1')
  await api.forgotPassword('person@example.com')
  await api.resetPassword('reset-token', 'NewPassword12')
  await api.refresh()

  expect(calls.map(({ method, url }) => [method, url])).toEqual([
    ['post', '/auth/register'],
    ['post', '/auth/verify-email'],
    ['post', '/auth/resend-verification'],
    ['post', '/auth/login'],
    ['post', '/auth/forgot-password'],
    ['post', '/auth/reset-password'],
    ['post', '/auth/refresh'],
  ])
  expect(JSON.parse(calls[0].data!)).toEqual({
    email: 'person@example.com',
    password: 'LongPassword1',
  })
  expect(JSON.parse(calls[5].data!)).toEqual({
    token: 'reset-token',
    newPassword: 'NewPassword12',
  })
})
```

- [ ] **Step 2: Run the test to verify RED**

Run: `cd client/web && npm test -- src/features/auth/api/auth-api.test.ts`

Expected: FAIL because `auth-api.ts` does not exist.

- [ ] **Step 3: Implement the minimal adapter**

```ts
import type { AxiosInstance } from 'axios'

export type AccessTokenResponse = {
  accessToken: string
  tokenType: string
  expiresIn: number
}

export type AcceptedResponse = { message: string }

export type AuthenticationApi = {
  register(email: string, password: string): Promise<AcceptedResponse>
  verifyEmail(token: string): Promise<void>
  resendVerification(email: string): Promise<AcceptedResponse>
  login(email: string, password: string): Promise<AccessTokenResponse>
  forgotPassword(email: string): Promise<AcceptedResponse>
  resetPassword(token: string, newPassword: string): Promise<void>
  refresh(): Promise<AccessTokenResponse>
}

export function createAuthenticationApi(client: AxiosInstance): AuthenticationApi {
  return {
    register: async (email, password) =>
      (await client.post<AcceptedResponse>('/auth/register', { email, password })).data,
    verifyEmail: async (token) => {
      await client.post('/auth/verify-email', { token })
    },
    resendVerification: async (email) =>
      (await client.post<AcceptedResponse>('/auth/resend-verification', { email })).data,
    login: async (email, password) =>
      (await client.post<AccessTokenResponse>('/auth/login', { email, password })).data,
    forgotPassword: async (email) =>
      (await client.post<AcceptedResponse>('/auth/forgot-password', { email })).data,
    resetPassword: async (token, newPassword) => {
      await client.post('/auth/reset-password', { token, newPassword })
    },
    refresh: async () => (await client.post<AccessTokenResponse>('/auth/refresh')).data,
  }
}
```

- [ ] **Step 4: Run the focused tests to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/api/auth-api.test.ts src/lib/api-client.test.ts`

Expected: both files PASS.

- [ ] **Step 5: Commit the adapter**

```bash
git add client/web/src/features/auth/api
git commit -m "feat(auth): add frontend authentication API"
```

---

### Task 2: Form validation and API field errors

**Files:**
- Create: `client/web/src/features/auth/forms/form-errors.ts`
- Create: `client/web/src/features/auth/forms/form-errors.test.ts`

**Interfaces:**
- Consumes: `ApiError` from `src/lib/api`.
- Produces: `validateEmail`, `validatePassword`, `validateConfirmation`, `fieldError`, and `pageError`.

- [ ] **Step 1: Write failing validation tests**

```ts
it.each([
  ['', 'Email is required.'],
  ['invalid', 'Enter a valid email address.'],
  [`${'a'.repeat(245)}@example.com`, 'Email must be 256 characters or fewer.'],
])('validates email %j', (value, expected) => {
  expect(validateEmail(value)).toBe(expected)
})

it('validates password length and confirmation', () => {
  expect(validatePassword('short')).toBe('Password must be at least 12 characters.')
  expect(validatePassword('a'.repeat(1025))).toBe('Password must be 1024 characters or fewer.')
  expect(validateConfirmation('one', 'two')).toBe('Passwords do not match.')
})

it('matches Problem Details field names case-insensitively', () => {
  const error = new ApiError('Invalid', {
    errors: { Email: ['Server email error'], NewPassword: ['Server password error'] },
  })
  expect(fieldError(error, 'email')).toBe('Server email error')
  expect(fieldError(error, 'newPassword')).toBe('Server password error')
  expect(pageError(error, ['email', 'newPassword'])).toBeUndefined()
})
```

- [ ] **Step 2: Run the test to verify RED**

Run: `cd client/web && npm test -- src/features/auth/forms/form-errors.test.ts`

Expected: FAIL because the helper does not exist.

- [ ] **Step 3: Implement exact validation and error mapping**

```ts
import { ApiError } from '../../../lib/api'

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export function validateEmail(value: string) {
  if (!value) return 'Email is required.'
  if (value.length > 256) return 'Email must be 256 characters or fewer.'
  if (!EMAIL.test(value)) return 'Enter a valid email address.'
}

export function validatePassword(value: string, minimum = 12) {
  if (!value) return 'Password is required.'
  if (value.length < minimum) return `Password must be at least ${minimum} characters.`
  if (value.length > 1024) return 'Password must be 1024 characters or fewer.'
}

export function validateConfirmation(password: string, confirmation: string) {
  if (!confirmation) return 'Confirm your password.'
  if (password !== confirmation) return 'Passwords do not match.'
}

export function fieldError(error: unknown, name: string) {
  if (!(error instanceof ApiError) || !error.errors) return undefined
  const key = Object.keys(error.errors).find(
    (candidate) => candidate.toLowerCase() === name.toLowerCase(),
  )
  return key ? error.errors[key][0] : undefined
}

export function pageError(error: unknown, fieldNames: string[]) {
  if (!(error instanceof ApiError)) return 'Unable to complete the request.'
  const known = new Set(fieldNames.map((name) => name.toLowerCase()))
  const unmatched = Object.entries(error.errors ?? {})
    .filter(([name]) => !known.has(name.toLowerCase()))
    .flatMap(([, messages]) => messages)
  return unmatched[0] ?? (Object.keys(error.errors ?? {}).length ? undefined : error.detail)
}
```

- [ ] **Step 4: Run the test to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/forms/form-errors.test.ts`

Expected: PASS.

- [ ] **Step 5: Commit validation helpers**

```bash
git add client/web/src/features/auth/forms
git commit -m "feat(auth): validate authentication forms"
```

---

### Task 3: In-memory authentication and silent refresh

**Files:**
- Create: `client/web/src/features/auth/state/AuthContext.ts`
- Create: `client/web/src/features/auth/state/AuthProvider.tsx`
- Create: `client/web/src/features/auth/state/AuthProvider.test.tsx`

**Interfaces:**
- Consumes: `AuthenticationApi`, `createApiClient`, `readClientConfig`.
- Produces: `AuthStatus = 'initializing' | 'authenticated' | 'anonymous'`, `AuthContextValue`, `AuthProvider`, and `useAuth()`.

- [ ] **Step 1: Write failing lifecycle tests**

Use a deferred promise and a probe component that renders `status`:

```tsx
it('hides authentication until startup refresh resolves', async () => {
  const pending = Promise.withResolvers<AccessTokenResponse>()
  const api = fakeApi({ refresh: vi.fn(() => pending.promise) })
  render(<AuthProvider api={api}><StatusProbe /></AuthProvider>)
  expect(screen.getByText('initializing')).toBeVisible()
  pending.resolve({ accessToken: 'secret', tokenType: 'Bearer', expiresIn: 300 })
  expect(await screen.findByText('authenticated')).toBeVisible()
  expect(screen.queryByText('secret')).not.toBeInTheDocument()
})

it('becomes anonymous when startup refresh fails', async () => {
  const api = fakeApi({ refresh: vi.fn().mockRejectedValue(new Error('401')) })
  render(<AuthProvider api={api}><StatusProbe /></AuthProvider>)
  expect(await screen.findByText('anonymous')).toBeVisible()
})

it('performs one effective startup refresh under Strict Mode', async () => {
  const refresh = vi.fn().mockResolvedValue({
    accessToken: 'secret', tokenType: 'Bearer', expiresIn: 300,
  })
  render(<StrictMode><AuthProvider api={fakeApi({ refresh })}><StatusProbe /></AuthProvider></StrictMode>)
  expect(await screen.findByText('authenticated')).toBeVisible()
  expect(refresh).toHaveBeenCalledTimes(1)
})

it('shares one refresh and clears the session when scheduled refresh fails', async () => {
  vi.useFakeTimers()
  const refresh = vi.fn()
    .mockResolvedValueOnce({ accessToken: 'first', tokenType: 'Bearer', expiresIn: 31 })
    .mockRejectedValueOnce(new Error('expired'))
  render(<AuthProvider api={fakeApi({ refresh })}><StatusProbe /></AuthProvider>)
  await act(async () => {})
  await vi.advanceTimersByTimeAsync(1_000)
  expect(refresh).toHaveBeenCalledTimes(2)
  expect(screen.getByText('anonymous')).toBeVisible()
  expect(localStorage).toHaveLength(0)
  expect(sessionStorage).toHaveLength(0)
})
```

Add the successful timer and login cases explicitly:

```tsx
it('stays authenticated after scheduled refresh succeeds', async () => {
  vi.useFakeTimers()
  const refresh = vi.fn()
    .mockResolvedValueOnce({ accessToken: 'first', tokenType: 'Bearer', expiresIn: 31 })
    .mockResolvedValueOnce({ accessToken: 'second', tokenType: 'Bearer', expiresIn: 60 })
  render(<AuthProvider api={fakeApi({ refresh })}><StatusProbe /></AuthProvider>)
  await act(async () => {})
  await vi.advanceTimersByTimeAsync(1_000)
  expect(refresh).toHaveBeenCalledTimes(2)
  expect(screen.getByText('authenticated')).toBeVisible()
})

it('logs in without exposing or persisting the access token', async () => {
  const login = vi.fn().mockResolvedValue({
    accessToken: 'private-token', tokenType: 'Bearer', expiresIn: 300,
  })
  render(<AuthProvider api={fakeApi({
    refresh: vi.fn().mockRejectedValue(new Error('anonymous')),
    login,
  })}><LoginProbe /></AuthProvider>)
  await screen.findByText('anonymous')
  await userEvent.click(screen.getByRole('button', { name: 'Test login' }))
  expect(login).toHaveBeenCalledWith('person@example.com', 'password')
  expect(await screen.findByText('authenticated')).toBeVisible()
  expect(screen.queryByText('private-token')).not.toBeInTheDocument()
  expect(localStorage).toHaveLength(0)
  expect(sessionStorage).toHaveLength(0)
})
```

- [ ] **Step 2: Run the provider tests to verify RED**

Run: `cd client/web && npm test -- src/features/auth/state/AuthProvider.test.tsx`

Expected: FAIL because the provider files do not exist.

- [ ] **Step 3: Define the public context contract**

```ts
import { createContext, useContext } from 'react'
import type { AuthenticationApi } from '../api/auth-api'

export type AuthStatus = 'initializing' | 'authenticated' | 'anonymous'
export type AuthContextValue = {
  status: AuthStatus
  api: AuthenticationApi
  login(email: string, password: string): Promise<void>
  logoutLocal(): void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth() {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used within AuthProvider')
  return value
}
```

- [ ] **Step 4: Implement the refresh lifecycle**

`AuthProvider` accepts `api?: AuthenticationApi` for tests. Without it, create
the default API once from `readClientConfig(import.meta.env).apiBaseUrl`, set
`client.defaults.withCredentials = true`, and supply a header callback reading
`tokenRef.current`.

Use these exact lifecycle rules:

```tsx
const REFRESH_EARLY_MS = 30_000
const MIN_REFRESH_DELAY_MS = 1_000

const applyToken = useCallback((result: AccessTokenResponse) => {
  tokenRef.current = result.accessToken
  setStatus('authenticated')
  window.clearTimeout(timerRef.current)
  const delay = Math.max(
    MIN_REFRESH_DELAY_MS,
    result.expiresIn * 1_000 - REFRESH_EARLY_MS,
  )
  timerRef.current = window.setTimeout(
    () => void refreshRef.current(),
    delay,
  )
}, [])

const runRefresh = useCallback(() => {
  if (refreshPromise.current) return refreshPromise.current
  const request = authApi.refresh()
    .then(applyToken)
    .catch(() => clearSession())
    .finally(() => { refreshPromise.current = null })
  refreshPromise.current = request
  return request
}, [applyToken, authApi, clearSession])

refreshRef.current = runRefresh
```

Store `runRefresh` in `refreshRef` so the timer always invokes the current
callback without a circular hook dependency. Guard startup with
`initializedRef.current` so React Strict
Mode effect replay performs one effective request. Cleanup clears only the
timer; request failures are normalized to `anonymous`. `login` calls
`authApi.login`, then applies the result. `logoutLocal` calls `clearSession`.

- [ ] **Step 5: Run lifecycle tests to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/state/AuthProvider.test.tsx`

Expected: PASS with no unhandled promise warnings.

- [ ] **Step 6: Run API and state tests together**

Run: `cd client/web && npm test -- src/features/auth/api src/features/auth/state`

Expected: PASS.

- [ ] **Step 7: Commit auth state**

```bash
git add client/web/src/features/auth/state
git commit -m "feat(auth): manage frontend authentication state"
```

---

### Task 4: Protected routing and safe return paths

**Files:**
- Create: `client/web/src/features/auth/routing/auth-return-path.ts`
- Create: `client/web/src/features/auth/routing/auth-return-path.test.ts`
- Create: `client/web/src/features/auth/routing/ProtectedRoute.tsx`
- Create: `client/web/src/features/auth/routing/ProtectedRoute.test.tsx`

**Interfaces:**
- Consumes: `useAuth()`, React Router location and navigation.
- Produces: `safeReturnPath(value, fallback?)` and `ProtectedRoute`.

- [ ] **Step 1: Write failing return-path tests**

```ts
it.each(['/app', '/app?view=mine#today'])('allows internal path %s', (path) => {
  expect(safeReturnPath(path)).toBe(path)
})

it.each([
  'https://evil.example', '//evil.example', '/\\evil', 'app',
  '/login', '/register', '/forgot-password', '/reset-password',
  '/auth/google/callback',
])('rejects unsafe return path %s', (path) => {
  expect(safeReturnPath(path)).toBe('/app')
})
```

- [ ] **Step 2: Run the return-path test to verify RED**

Run: `cd client/web && npm test -- src/features/auth/routing/auth-return-path.test.ts`

Expected: FAIL because the helper does not exist.

- [ ] **Step 3: Implement the allow-list**

```ts
const AUTH_PATHS = new Set([
  '/login', '/register', '/forgot-password', '/reset-password',
  '/verify-email', '/auth/google/callback',
])

export function safeReturnPath(value: string | null | undefined, fallback = '/app') {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) {
    return fallback
  }
  let url: URL
  try {
    url = new URL(value, window.location.origin)
  } catch {
    return fallback
  }
  return url.origin === window.location.origin && !AUTH_PATHS.has(url.pathname)
    ? `${url.pathname}${url.search}${url.hash}`
    : fallback
}
```

- [ ] **Step 4: Run the allow-list test to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/routing/auth-return-path.test.ts`

Expected: PASS.

- [ ] **Step 5: Write failing protected-route tests**

Render routes with a supplied auth context and assert:

```tsx
it('does not render private content while initializing', () => {
  renderRoute('initializing', '/app?view=mine#today')
  expect(screen.getByRole('status')).toHaveTextContent('Checking your session')
  expect(screen.queryByText('Private content')).not.toBeInTheDocument()
})

it('redirects anonymous users with the complete return path', async () => {
  renderRoute('anonymous', '/app?view=mine#today')
  expect(await screen.findByTestId('location')).toHaveTextContent(
    '/login?returnTo=%2Fapp%3Fview%3Dmine%23today',
  )
})

it('renders children for authenticated users', () => {
  renderRoute('authenticated', '/app')
  expect(screen.getByText('Private content')).toBeVisible()
})
```

- [ ] **Step 6: Run the protected-route tests to verify RED**

Run: `cd client/web && npm test -- src/features/auth/routing/ProtectedRoute.test.tsx`

Expected: FAIL because `ProtectedRoute` does not exist.

- [ ] **Step 7: Implement the route boundary**

```tsx
export function ProtectedRoute({ children }: PropsWithChildren) {
  const { status } = useAuth()
  const location = useLocation()
  if (status === 'initializing') {
    return <div role="status"><Spinner /> Checking your session…</div>
  }
  if (status === 'anonymous') {
    const returnTo = `${location.pathname}${location.search}${location.hash}`
    return <Navigate replace to={`/login?returnTo=${encodeURIComponent(returnTo)}`} />
  }
  return children
}
```

- [ ] **Step 8: Run all routing tests to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/routing`

Expected: PASS.

- [ ] **Step 9: Commit routing**

```bash
git add client/web/src/features/auth/routing
git commit -m "feat(auth): protect authenticated routes"
```

---

### Task 5: Registration and email verification pages

**Files:**
- Create: `client/web/src/features/auth/pages/RegisterPage.tsx`
- Create: `client/web/src/features/auth/pages/RegisterPage.test.tsx`
- Create: `client/web/src/features/auth/pages/VerifyEmailPage.tsx`
- Create: `client/web/src/features/auth/pages/VerifyEmailPage.test.tsx`
- Create: `client/web/src/features/auth/pages/AuthPages.scss`

**Interfaces:**
- Consumes: `useAuth().api`, form helpers, existing UI kit and `AuthLayout`.
- Produces: `RegisterPage` and `VerifyEmailPage` route elements.

- [ ] **Step 1: Write failing registration tests**

Cover local validation, one API call during duplicate clicks, server field
errors, and neutral success:

```tsx
it('validates before registering and shows the neutral success', async () => {
  const user = userEvent.setup()
  const api = renderAuthPage(<RegisterPage />)
  await user.click(screen.getByRole('button', { name: 'Create account' }))
  expect(screen.getByText('Email is required.')).toBeVisible()
  expect(api.register).not.toHaveBeenCalled()

  await user.type(screen.getByLabelText('Email'), 'person@example.com')
  await user.type(screen.getByLabelText('Password'), 'LongPassword1')
  await user.type(screen.getByLabelText('Confirm password'), 'LongPassword1')
  await user.click(screen.getByRole('button', { name: 'Create account' }))
  expect(api.register).toHaveBeenCalledWith('person@example.com', 'LongPassword1')
  expect(await screen.findByRole('alert')).toHaveTextContent('check your email')
})
```

Use an `ApiError` with `{ Email: ['Already rejected safely'] }` to prove
case-insensitive field mapping.

- [ ] **Step 2: Run registration tests to verify RED**

Run: `cd client/web && npm test -- src/features/auth/pages/RegisterPage.test.tsx`

Expected: FAIL because the page does not exist.

- [ ] **Step 3: Implement registration**

Use controlled `email`, `password`, and `confirmation` state. On submit, run
`validateEmail`, `validatePassword(password, 12)`, and
`validateConfirmation`. If valid, set `submitting`, call
`api.register(email, password)`, render its `message` in a success `Alert`, and
always clear `submitting` in `finally`. Render server `email` and `password`
messages in `FormField`; send unmatched errors to an error `Alert`. Disable all
fields and the submit button while submitting. Footer links to `/login`.

The submit handler is:

```tsx
async function submit(event: FormEvent) {
  event.preventDefault()
  const nextErrors = {
    email: validateEmail(email),
    password: validatePassword(password),
    confirmation: validateConfirmation(password, confirmation),
  }
  setErrors(nextErrors)
  if (Object.values(nextErrors).some(Boolean)) return
  setSubmitting(true)
  setRequestError(undefined)
  try {
    setMessage((await api.register(email, password)).message)
  } catch (error) {
    setErrors({
      email: fieldError(error, 'email'),
      password: fieldError(error, 'password'),
    })
    setRequestError(pageError(error, ['email', 'password']))
  } finally {
    setSubmitting(false)
  }
}
```

Add shared page styles with only these rules:

```scss
.td-auth-form { display: grid; gap: var(--space-4); }
.td-auth-form__actions { display: grid; gap: var(--space-3); margin-top: var(--space-2); }
.td-auth-form__links { display: flex; justify-content: space-between; gap: var(--space-3); font-size: var(--font-size-caption); }
.td-auth-divider { display: flex; align-items: center; gap: var(--space-3); color: var(--color-text-muted); font-size: var(--font-size-caption); }
.td-auth-divider::before, .td-auth-divider::after { height: 1px; flex: 1; background: var(--color-border); content: ''; }
```

- [ ] **Step 4: Run registration tests to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/pages/RegisterPage.test.tsx`

Expected: PASS.

- [ ] **Step 5: Write failing verification tests**

Test automatic single submission, missing token, failure followed by resend,
and success:

```tsx
it('verifies the URL token once and links to login', async () => {
  const api = renderAuthPage(<VerifyEmailPage />, '/verify-email?token=abc%2F123')
  expect(await screen.findByText('Email verified')).toBeVisible()
  expect(api.verifyEmail).toHaveBeenCalledTimes(1)
  expect(api.verifyEmail).toHaveBeenCalledWith('abc/123')
  expect(screen.getByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/login')
})
```

- [ ] **Step 6: Run verification tests to verify RED**

Run: `cd client/web && npm test -- src/features/auth/pages/VerifyEmailPage.test.tsx`

Expected: FAIL because the page does not exist.

- [ ] **Step 7: Implement verification and resend**

Read `token` from `useSearchParams`. Guard the effect with a ref. Reject a
missing token or a token longer than 512 characters locally. During the request,
render a labelled status. Success renders `Email verified` and `/login`. Failure
renders the safe `ApiError.detail` plus an email resend form using the same
validation and submission rules as registration. Display the API's neutral
resend message on success.

The verification effect uses this shape so it cannot double-submit:

```tsx
useEffect(() => {
  if (started.current) return
  started.current = true
  if (!token || token.length > 512) {
    setState('error')
    setRequestError('The verification link is invalid or expired.')
    return
  }
  void api.verifyEmail(token)
    .then(() => setState('success'))
    .catch((error) => {
      setRequestError(pageError(error, ['token']))
      setState('error')
    })
}, [api, token])
```

- [ ] **Step 8: Run both page tests to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/pages/RegisterPage.test.tsx src/features/auth/pages/VerifyEmailPage.test.tsx`

Expected: PASS.

- [ ] **Step 9: Commit registration and verification**

```bash
git add client/web/src/features/auth/pages/RegisterPage* client/web/src/features/auth/pages/VerifyEmailPage* client/web/src/features/auth/pages/AuthPages.scss
git commit -m "feat(auth): add registration and verification pages"
```

---

### Task 6: Login and inert Google callback

**Files:**
- Create: `client/web/src/features/auth/pages/LoginPage.tsx`
- Create: `client/web/src/features/auth/pages/LoginPage.test.tsx`
- Create: `client/web/src/features/auth/pages/GoogleCallbackPage.tsx`
- Create: `client/web/src/features/auth/pages/GoogleCallbackPage.test.tsx`

**Interfaces:**
- Consumes: `useAuth().login`, `safeReturnPath`, auth form helpers.
- Produces: `LoginPage` and `GoogleCallbackPage` route elements.

- [ ] **Step 1: Write failing login tests**

Test local validation, generic unauthorized error, safe return navigation,
unsafe fallback, and disabled Google UI:

```tsx
it('logs in and returns to a safe requested route', async () => {
  const user = userEvent.setup()
  const auth = renderLogin('/login?returnTo=%2Fapp%3Fview%3Dmine')
  await user.type(screen.getByLabelText('Email'), 'person@example.com')
  await user.type(screen.getByLabelText('Password'), 'password')
  await user.click(screen.getByRole('button', { name: 'Sign in' }))
  expect(auth.login).toHaveBeenCalledWith('person@example.com', 'password')
  expect(await screen.findByTestId('location')).toHaveTextContent('/app?view=mine')
})

it('keeps Google unavailable without starting OAuth', () => {
  renderLogin('/login')
  expect(screen.getByRole('button', { name: /Google/ })).toBeDisabled()
  expect(screen.getByText('Chưa khả dụng')).toBeVisible()
})
```

- [ ] **Step 2: Run login tests to verify RED**

Run: `cd client/web && npm test -- src/features/auth/pages/LoginPage.test.tsx`

Expected: FAIL because the page does not exist.

- [ ] **Step 3: Implement login**

Use controlled email/password fields. Validate email and require a non-empty
password no longer than 1024 characters; login does not impose the 12-character
registration minimum. Call `login`, then navigate with `replace: true` to
`safeReturnPath(searchParams.get('returnTo'))`. For any rejected login without
field errors, display `Email or password is incorrect.`. Add links to
`/forgot-password` and `/register`. Render a full-width secondary Google button
with `disabled`, accessible name `Continue with Google — unavailable`, and
visible `Chưa khả dụng` text. Give it no click handler.

The successful branch is:

```tsx
try {
  await login(email, password)
  navigate(safeReturnPath(searchParams.get('returnTo')), { replace: true })
} catch {
  setRequestError('Email or password is incorrect.')
} finally {
  setSubmitting(false)
}
```

The Google control is static:

```tsx
<Button
  variant="secondary"
  fullWidth
  disabled
  aria-label="Continue with Google — unavailable"
>
  Continue with Google · Chưa khả dụng
</Button>
```

- [ ] **Step 4: Run login tests to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/pages/LoginPage.test.tsx`

Expected: PASS.

- [ ] **Step 5: Write the failing callback test**

```tsx
it('does not consume or render callback parameters', () => {
  renderPage('/auth/google/callback?code=private-code&state=private-state')
  expect(screen.getByText('Google sign-in is not configured')).toBeVisible()
  expect(screen.queryByText(/private-code|private-state/)).not.toBeInTheDocument()
  expect(localStorage).toHaveLength(0)
  expect(sessionStorage).toHaveLength(0)
})
```

- [ ] **Step 6: Run callback test to verify RED**

Run: `cd client/web && npm test -- src/features/auth/pages/GoogleCallbackPage.test.tsx`

Expected: FAIL because the page does not exist.

- [ ] **Step 7: Implement the inert callback page**

Render `AuthLayout` with static copy only: heading
`Google sign-in is not configured`, description that Google will be available
after server integration, and a `/login` link. Do not call `useLocation`,
`useSearchParams`, storage, or any auth API.

```tsx
export function GoogleCallbackPage() {
  return (
    <AuthLayout
      title="Google sign-in is not configured"
      description="Google sign-in will be available after server integration."
    >
      <Link to="/login">Back to sign in</Link>
    </AuthLayout>
  )
}
```

- [ ] **Step 8: Run login and callback tests to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/pages/LoginPage.test.tsx src/features/auth/pages/GoogleCallbackPage.test.tsx`

Expected: PASS.

- [ ] **Step 9: Commit login pages**

```bash
git add client/web/src/features/auth/pages/LoginPage* client/web/src/features/auth/pages/GoogleCallbackPage*
git commit -m "feat(auth): add login experience"
```

---

### Task 7: Forgot and reset password pages

**Files:**
- Create: `client/web/src/features/auth/pages/ForgotPasswordPage.tsx`
- Create: `client/web/src/features/auth/pages/ForgotPasswordPage.test.tsx`
- Create: `client/web/src/features/auth/pages/ResetPasswordPage.tsx`
- Create: `client/web/src/features/auth/pages/ResetPasswordPage.test.tsx`

**Interfaces:**
- Consumes: `useAuth().api`, form helpers, URL token.
- Produces: `ForgotPasswordPage` and `ResetPasswordPage` route elements.

- [ ] **Step 1: Write failing forgot-password tests**

Test invalid email blocks the request, valid email displays the API's neutral
message, and duplicate submit is disabled while pending.

```tsx
expect(api.forgotPassword).toHaveBeenCalledWith('person@example.com')
expect(await screen.findByRole('alert')).toHaveTextContent(
  'If the account exists, a password reset email will be sent.',
)
```

- [ ] **Step 2: Run forgot-password tests to verify RED**

Run: `cd client/web && npm test -- src/features/auth/pages/ForgotPasswordPage.test.tsx`

Expected: FAIL because the page does not exist.

- [ ] **Step 3: Implement forgot-password**

Build a one-field email form with `validateEmail`, `FormField`, loading button,
page-level `Alert`, and a `/login` footer link. Display the accepted message
verbatim and do not branch on whether the account exists.

```tsx
async function submit(event: FormEvent) {
  event.preventDefault()
  const emailError = validateEmail(email)
  setEmailError(emailError)
  if (emailError) return
  setSubmitting(true)
  try {
    setMessage((await api.forgotPassword(email)).message)
  } catch (error) {
    setRequestError(pageError(error, ['email']))
  } finally {
    setSubmitting(false)
  }
}
```

- [ ] **Step 4: Run forgot-password tests to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/pages/ForgotPasswordPage.test.tsx`

Expected: PASS.

- [ ] **Step 5: Write failing reset-password tests**

Test missing/oversized token blocks the API, password validation and
confirmation, server `NewPassword` field mapping, and successful login link:

```tsx
it('resets with the URL token and shows the login action', async () => {
  const user = userEvent.setup()
  const api = renderAuthPage(<ResetPasswordPage />, '/reset-password?token=reset%2Ftoken')
  await user.type(screen.getByLabelText('New password'), 'NewPassword12')
  await user.type(screen.getByLabelText('Confirm password'), 'NewPassword12')
  await user.click(screen.getByRole('button', { name: 'Reset password' }))
  expect(api.resetPassword).toHaveBeenCalledWith('reset/token', 'NewPassword12')
  expect(await screen.findByRole('link', { name: 'Sign in' })).toHaveAttribute('href', '/login')
})
```

- [ ] **Step 6: Run reset-password tests to verify RED**

Run: `cd client/web && npm test -- src/features/auth/pages/ResetPasswordPage.test.tsx`

Expected: FAIL because the page does not exist.

- [ ] **Step 7: Implement reset-password**

Read the token from `useSearchParams`. Render a local error immediately when it
is absent or longer than 512 characters. For a valid token, render new-password
and confirmation fields, validate with the 12/1024 limits and equality, then
call `api.resetPassword(token, password)`. Map server `newPassword` errors to the
password field and `token` errors to a page alert. On success replace the form
with `Password reset` and a `/login` link.

```tsx
async function submit(event: FormEvent) {
  event.preventDefault()
  const passwordError = validatePassword(password)
  const confirmationError = validateConfirmation(password, confirmation)
  setErrors({ password: passwordError, confirmation: confirmationError })
  if (!token || token.length > 512 || passwordError || confirmationError) return
  setSubmitting(true)
  try {
    await api.resetPassword(token, password)
    setComplete(true)
  } catch (error) {
    setErrors({ password: fieldError(error, 'newPassword') })
    setRequestError(fieldError(error, 'token') ?? pageError(error, ['newPassword']))
  } finally {
    setSubmitting(false)
  }
}
```

- [ ] **Step 8: Run password page tests to verify GREEN**

Run: `cd client/web && npm test -- src/features/auth/pages/ForgotPasswordPage.test.tsx src/features/auth/pages/ResetPasswordPage.test.tsx`

Expected: PASS.

- [ ] **Step 9: Commit recovery pages**

```bash
git add client/web/src/features/auth/pages/ForgotPasswordPage* client/web/src/features/auth/pages/ResetPasswordPage*
git commit -m "feat(auth): add password recovery pages"
```

---

### Task 8: Application wiring and completion gates

**Files:**
- Create: `client/web/src/app/AuthenticatedHome.tsx`
- Modify: `client/web/src/App.tsx`
- Modify: `client/web/src/App.test.tsx`
- Modify: `client/web/src/main.tsx`
- Modify: `client/web/vite.config.ts`

**Interfaces:**
- Consumes: all pages, `AuthProvider`, and `ProtectedRoute` from Tasks 3-7.
- Produces: navigable public auth routes and protected `/app`.

- [ ] **Step 1: Write failing application-route tests**

Refactor the test render helper to supply `AuthContext`. Preserve existing UI
kit, not-found, error-boundary, and invalid-config tests. Add:

```tsx
it.each([
  ['/register', 'Create your account'],
  ['/login', 'Sign in'],
  ['/forgot-password', 'Forgot your password?'],
  ['/reset-password', 'Reset your password'],
  ['/auth/google/callback', 'Google sign-in is not configured'],
])('renders %s', (path, heading) => {
  renderApp(path, 'anonymous')
  expect(screen.getByRole('heading', { name: heading })).toBeVisible()
})

it('protects /app', async () => {
  renderApp('/app', 'anonymous')
  expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeVisible()
})

it('renders /app after authentication', () => {
  renderApp('/app', 'authenticated')
  expect(screen.getByRole('heading', { name: 'Your workspace' })).toBeVisible()
})
```

- [ ] **Step 2: Run application tests to verify RED**

Run: `cd client/web && npm test -- src/App.test.tsx`

Expected: FAIL because the routes are not wired.

- [ ] **Step 3: Add the private destination**

`AuthenticatedHome.tsx` renders the existing `AppShell` visual language with a
heading `Your workspace` and one sentence explaining that the private workspace
is ready. Move `AppShell` from
`App.tsx` into a named local component only if the private page needs it;
otherwise render the private content through the existing shell route element.
Do not add dashboard data or logout behavior.

- [ ] **Step 4: Wire routes**

Add exact route entries:

```tsx
<Route path="/register" element={<RegisterPage />} />
<Route path="/verify-email" element={<VerifyEmailPage />} />
<Route path="/login" element={<LoginPage />} />
<Route path="/forgot-password" element={<ForgotPasswordPage />} />
<Route path="/reset-password" element={<ResetPasswordPage />} />
<Route path="/auth/google/callback" element={<GoogleCallbackPage />} />
<Route path="/app" element={
  <ProtectedRoute><AppShell><AuthenticatedHome /></AppShell></ProtectedRoute>
} />
```

Keep `/`, development-only `/ui-kit`, and `*` unchanged.

- [ ] **Step 5: Mount the provider**

In `main.tsx`, mount it inside the router so navigation hooks remain available:

```tsx
<BrowserRouter>
  <AuthProvider>
    <App />
  </AuthProvider>
</BrowserRouter>
```

No token value is passed through props or serialized into HTML.

- [ ] **Step 6: Extend coverage ownership**

Add `'src/features/**'` to the `coverage.include` array in `vite.config.ts`.

- [ ] **Step 7: Run application and auth tests to verify GREEN**

Run: `cd client/web && npm test -- src/App.test.tsx src/features/auth`

Expected: PASS.

- [ ] **Step 8: Run the full frontend test suite**

Run: `cd client/web && npm test`

Expected: all tests PASS with no warnings or unhandled rejections.

- [ ] **Step 9: Run static quality gates**

Run: `cd client/web && npm run lint && npm run format:check && npm run build`

Expected: all commands exit 0. If formatting fails, run
`cd client/web && npm run format`, inspect the changed auth files, then repeat
the three gates.

- [ ] **Step 10: Verify the security constraints in the diff**

Run:

```bash
rg -n "localStorage|sessionStorage|console\.|accessToken|refreshToken" client/web/src/features/auth client/web/src/App.tsx client/web/src/main.tsx
git diff --check
git status --short
```

Expected: storage references exist only in negative tests; `accessToken` exists
only in typed API/state handling; no refresh token is read by client code; no
console logging exists; `git diff --check` is silent; status contains only the
intended SCRUM-28 files.

- [ ] **Step 11: Commit application wiring**

```bash
git add client/web/src/App.tsx client/web/src/App.test.tsx client/web/src/main.tsx client/web/src/app/AuthenticatedHome.tsx client/web/vite.config.ts
git commit -m "feat(auth): wire frontend authentication flows"
```

- [ ] **Step 12: Review the complete ticket diff**

Run: `git diff 3b48327^..HEAD --stat && git log --oneline 3b48327^..HEAD`

Expected: one design commit plus focused API, validation, state, routing, page,
and wiring commits; no server or dependency-lock changes.
