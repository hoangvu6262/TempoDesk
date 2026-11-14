# English Frontend Error Messages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every user-facing frontend error and failure message with the approved English copy.

**Architecture:** Reusable safe API messages live in one typed constant object. Component-specific failure copy stays beside its component; tests lock accessible English output and API safety behavior.

**Tech Stack:** React 19, TypeScript 6, Axios, Vitest 5, Testing Library.

## Global Constraints

- Translate API errors, ErrorBoundary, invalid configuration, HealthStatus, and UI Kit error examples.
- Keep valid server-provided Problem Details `detail` unchanged.
- Do not translate unrelated product copy.
- Do not add i18n, TanStack Query, or dependencies.
- Do not duplicate reusable API error strings outside `src/constants/errors.ts`.

---

### Task 1: English error copy

**Files:**

- Create: `client/web/src/constants/errors.ts`
- Modify: `client/web/src/lib/api/api-error.ts`
- Modify: `client/web/src/lib/api/api-path.ts`
- Modify: `client/web/src/lib/api/api-client.ts`
- Modify: `client/web/src/lib/api-client.test.ts`
- Modify: `client/web/src/app/ErrorBoundary.tsx`
- Modify: `client/web/src/app/HealthStatus.tsx`
- Modify: `client/web/src/app/HealthStatus.test.tsx`
- Modify: `client/web/src/App.tsx`
- Modify: `client/web/src/App.test.tsx`
- Modify: `client/web/src/app/UiKitPage.tsx`
- Modify: `client/web/src/app/UiKitPage.test.tsx`

**Interfaces:**

- Produces: `API_ERROR_MESSAGE` with `cancelled`, `invalidPath`, `invalidResponse`, `invalidTimeout`, `network`, `requestFailed`, and `timeout` keys.
- Preserves: `ApiError` fields and all error kinds.

- [ ] **Step 1: Change test assertions to the approved English copy**

Use these exact assertions:

```text
Unable to complete the request.
The request was cancelled.
The API response is invalid.
Something went wrong
Application configuration is invalid
Checking API
API is ready
API is unavailable
The health response is invalid
Invalid email
```

Keep the server Problem Details test value `Safe detail` unchanged.

- [ ] **Step 2: Run focused tests to verify RED**

Run:

```bash
npm --prefix client/web run test -- src/lib/api-client.test.ts src/app/HealthStatus.test.tsx src/App.test.tsx src/app/UiKitPage.test.tsx
```

Expected: FAIL because production code still renders Vietnamese safe error copy.

- [ ] **Step 3: Add reusable API messages**

Create `client/web/src/constants/errors.ts`:

```ts
export const API_ERROR_MESSAGE = {
  cancelled: 'The request was cancelled.',
  invalidPath: 'The API path is invalid.',
  invalidResponse: 'The API response is invalid.',
  invalidTimeout: 'The request timeout is invalid.',
  network: 'Unable to connect to the API.',
  requestFailed: 'Unable to complete the request.',
  timeout: 'The API request timed out.',
} as const
```

Replace every matching literal in `api-error.ts`, `api-path.ts`, and `api-client.ts` with `API_ERROR_MESSAGE.<key>`. Preserve server-provided `problem.detail` when it is a string.

- [ ] **Step 4: Translate application fallback and status copy**

Use these exact values:

```text
ErrorBoundary: Something went wrong / Reload the page and try again. / Back to home
Invalid config: Application configuration is invalid / Check VITE_API_BASE_URL and VITE_SIGNALR_BASE_URL, then restart the application.
Health: Checking API… / API is ready / API is unavailable / The health response is invalid / Check the API connection and database status.
UI Kit: Invalid email / Enter a valid email address. / Error / Unable to complete the action.
```

- [ ] **Step 5: Verify GREEN and all frontend gates**

Run:

```bash
npm --prefix client/web run format
npm --prefix client/web run lint
npm --prefix client/web run test -- --coverage
npm --prefix client/web run build
```

Expected: format/lint/build exit `0`; all tests pass with coverage.

- [ ] **Step 6: Confirm no Vietnamese error copy remains**

Run:

```bash
rg -n 'lỗi|Lỗi|không hợp lệ|Không thể|Đã có lỗi|quá thời gian|đã được hủy|chưa sẵn sàng' client/web/src --glob '*.{ts,tsx}'
```

Expected: no matches in production code or test expectations.

- [ ] **Step 7: Commit**

```bash
git add client/web/src
git commit -m "refactor(frontend): use English error messages"
```
