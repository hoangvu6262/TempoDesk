# SCRUM-31 Design System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build TempoDesk's Twenty-inspired, teal design-system foundation with runtime light/dark themes, shared accessible components, AuthLayout, and a development UI Kit.

**Architecture:** Keep the existing React 19/Vite application and add a small `src/ui` layer. Runtime theme values are semantic CSS custom properties authored in global SCSS; reusable components use `td-`-prefixed BEM blocks and keep business state in their callers. React Context owns only the persisted theme preference, Base UI supplies Checkbox behavior, and native HTML supplies the remaining primitives.

**Tech Stack:** React 19, TypeScript 6, Vite 8, global SCSS, BEM, Base UI, Tabler Icons, Inter, Vitest, Testing Library.

## Global Constraints

- Treat Twenty as a structural and visual reference; do not copy code from the AGPL-licensed `twenty-front` package.
- Do not depend on `twenty-ui`, Nx, Linaria, Jotai, Storybook, or a generated token pipeline.
- Use global SCSS with one `td-`-prefixed BEM block per component; use `__` for elements and `--` for modifiers.
- Use semantic CSS custom properties for every component color, spacing, radius, shadow, control size, and motion value.
- Keep form values, validation, async work, and authentication state in callers. Theme preference is the only new shared state.
- Prefer controlled props; internal state is limited to private presentation behavior such as password visibility.
- Preserve native semantics, keyboard access, focus-visible styling, programmatic labels/errors, reduced motion, and accessible icon buttons.
- Add only `@base-ui/react`, `@tabler/icons-react`, `@fontsource/inter`, `@testing-library/user-event`, and `sass`.
- Test user-visible behavior with Testing Library and `user-event`; do not assert CSS class names.
- Keep existing health, environment, routing, and error-boundary behavior working.

---

## File Map

**Create:**

- `client/web/src/ui/tokens.scss` — semantic light/dark theme variables and reduced-motion override.
- `client/web/src/ui/theme/theme.ts` — theme preference parsing, resolution, storage, and initial DOM application.
- `client/web/src/ui/theme/ThemeProvider.tsx` — the only shared UI state provider and `useTheme` hook.
- `client/web/src/ui/theme/ThemeProvider.test.tsx` — stored/system theme behavior.
- `client/web/src/ui/components/*` — component `.tsx`, global `.scss`, and behavior tests for the ticket's ten components.
- `client/web/src/ui/layouts/AuthLayout/*` — authentication-only presentation shell and test.
- `client/web/src/ui/index.ts` — the single public UI export surface.
- `client/web/src/app/UiKitPage.tsx` and `UiKitPage.scss` — development catalog and theme controls.

**Modify:**

- `client/web/package.json` and `client/web/package-lock.json` — approved dependencies.
- `client/web/src/main.tsx` — font, SCSS, initial theme, and provider wiring.
- `client/web/src/App.tsx` and `App.test.tsx` — development-only UI Kit route and BEM app-shell classes.
- `client/web/src/app/ErrorBoundary.tsx` — BEM fallback classes only.
- `client/web/src/app/HealthStatus.tsx` — BEM health-status classes only.
- `client/web/vite.config.ts` — include `src/ui/**` in coverage.

**Rename:**

- `client/web/src/index.css` to `client/web/src/index.scss` — retain current layout behavior, then migrate selectors to tokens and BEM in the UI Kit integration task.

---

### Task 1: Dependencies, tokens, and theme state

**Files:**

- Modify: `client/web/package.json`
- Modify: `client/web/package-lock.json`
- Rename: `client/web/src/index.css` to `client/web/src/index.scss`
- Create: `client/web/src/ui/tokens.scss`
- Create: `client/web/src/ui/theme/theme.ts`
- Create: `client/web/src/ui/theme/ThemeProvider.tsx`
- Test: `client/web/src/ui/theme/ThemeProvider.test.tsx`
- Modify: `client/web/src/main.tsx`

**Interfaces:**

- Produces: `ThemePreference = 'light' | 'dark' | 'system'`.
- Produces: `ResolvedTheme = 'light' | 'dark'`.
- Produces: `resolveTheme(preference, systemIsDark): ResolvedTheme`.
- Produces: `readThemePreference(storage?): ThemePreference`.
- Produces: `applyInitialTheme(): void`.
- Produces: `ThemeProvider`, `useTheme(): { preference; resolvedTheme; setPreference }`.

- [ ] **Step 1: Install only the approved packages**

Run:

```bash
cd client/web
npm install @base-ui/react @tabler/icons-react @fontsource/inter
npm install --save-dev @testing-library/user-event sass
```

Expected: `package.json` and `package-lock.json` change; npm exits 0.

- [ ] **Step 2: Write failing theme tests**

Create `src/ui/theme/ThemeProvider.test.tsx` with tests that:

```tsx
import userEvent from '@testing-library/user-event'
import { render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ThemeProvider, useTheme } from './ThemeProvider'

function ThemeProbe() {
  const { preference, resolvedTheme, setPreference } = useTheme()
  return (
    <>
      <output role="status">{`${preference}:${resolvedTheme}`}</output>
      <button onClick={() => setPreference('dark')}>Dark</button>
    </>
  )
}

it('persists an explicit theme and applies it to the document', async () => {
  const user = userEvent.setup()
  render(<ThemeProvider><ThemeProbe /></ThemeProvider>)
  await user.click(screen.getByRole('button', { name: 'Dark' }))
  expect(screen.getByRole('status')).toHaveTextContent('dark:dark')
  expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
  expect(localStorage.getItem('tempodesk.theme')).toBe('dark')
})

it('follows the system only while the system preference is selected', () => {
  localStorage.setItem('tempodesk.theme', 'system')
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({
    matches: true,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  }))
  render(<ThemeProvider><ThemeProbe /></ThemeProvider>)
  expect(screen.getByRole('status')).toHaveTextContent('system:dark')
})
```

Add `localStorage.clear()`, `vi.unstubAllGlobals()`, and removal of `data-theme` to the shared `afterEach` in `src/test/setup.ts`, keeping every theme test isolated.

- [ ] **Step 3: Run the test and verify RED**

Run:

```bash
cd client/web
npm run test -- src/ui/theme/ThemeProvider.test.tsx
```

Expected: FAIL because `ThemeProvider` does not exist.

- [ ] **Step 4: Implement the theme utilities and provider**

Create `theme.ts` with one storage key, strict preference parsing, and synchronous initial application:

```ts
export type ThemePreference = 'light' | 'dark' | 'system'
export type ResolvedTheme = Exclude<ThemePreference, 'system'>

export const THEME_STORAGE_KEY = 'tempodesk.theme'

export function resolveTheme(
  preference: ThemePreference,
  systemIsDark: boolean,
): ResolvedTheme {
  return preference === 'system' ? (systemIsDark ? 'dark' : 'light') : preference
}

export function readThemePreference(
  storage: Pick<Storage, 'getItem'> = localStorage,
): ThemePreference {
  const value = storage.getItem(THEME_STORAGE_KEY)
  return value === 'light' || value === 'dark' || value === 'system'
    ? value
    : 'system'
}

export function applyInitialTheme() {
  const preference = readThemePreference()
  const systemIsDark = matchMedia('(prefers-color-scheme: dark)').matches
  document.documentElement.dataset.theme = resolveTheme(preference, systemIsDark)
}
```

Create `ThemeProvider.tsx` with a single context. Subscribe to `matchMedia` only when `preference === 'system'`; persist only inside `setPreference`; update `data-theme` whenever the resolved theme changes. Throw a clear error if `useTheme` is called outside the provider.

```tsx
import {
  createContext,
  type PropsWithChildren,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react'
import {
  readThemePreference,
  resolveTheme,
  THEME_STORAGE_KEY,
  type ResolvedTheme,
  type ThemePreference,
} from './theme'

type ThemeContextValue = {
  preference: ThemePreference
  resolvedTheme: ResolvedTheme
  setPreference: (preference: ThemePreference) => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

export function ThemeProvider({ children }: PropsWithChildren) {
  const [media] = useState(() => matchMedia('(prefers-color-scheme: dark)'))
  const [preference, setStoredPreference] = useState(readThemePreference)
  const [systemIsDark, setSystemIsDark] = useState(media.matches)
  const resolvedTheme = resolveTheme(preference, systemIsDark)

  useEffect(() => {
    if (preference !== 'system') return
    const onChange = (event: MediaQueryListEvent) => setSystemIsDark(event.matches)
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [media, preference])

  useEffect(() => {
    document.documentElement.dataset.theme = resolvedTheme
  }, [resolvedTheme])

  const value = useMemo<ThemeContextValue>(() => ({
    preference,
    resolvedTheme,
    setPreference(nextPreference) {
      localStorage.setItem(THEME_STORAGE_KEY, nextPreference)
      setStoredPreference(nextPreference)
    },
  }), [preference, resolvedTheme])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}

export function useTheme() {
  const value = useContext(ThemeContext)
  if (!value) throw new Error('useTheme must be used inside ThemeProvider')
  return value
}
```

- [ ] **Step 5: Add exact semantic tokens**

Create `tokens.scss` with this minimum stable surface:

```scss
:root,
[data-theme='light'] {
  color-scheme: light;
  --font-family-sans: 'Inter', system-ui, sans-serif;
  --font-size-caption: 0.75rem;
  --font-size-body: 0.875rem;
  --font-size-label: 0.8125rem;
  --font-size-heading-3: 1rem;
  --font-size-heading-2: 1.25rem;
  --font-size-heading-1: 1.75rem;
  --font-weight-regular: 400;
  --font-weight-medium: 500;
  --font-weight-semibold: 600;
  --line-height-tight: 1.25;
  --line-height-body: 1.5;
  --space-0: 0;
  --space-1: 0.25rem;
  --space-2: 0.5rem;
  --space-3: 0.75rem;
  --space-4: 1rem;
  --space-5: 1.25rem;
  --space-6: 1.5rem;
  --space-8: 2rem;
  --space-10: 2.5rem;
  --radius-small: 0.25rem;
  --radius-medium: 0.5rem;
  --radius-large: 1rem;
  --control-height-small: 2rem;
  --control-height-medium: 2.5rem;
  --icon-size-small: 0.875rem;
  --icon-size-medium: 1rem;
  --motion-fast: 120ms;
  --color-background: #f6f8f8;
  --color-surface: #ffffff;
  --color-surface-muted: #f0f4f3;
  --color-surface-hover: #e8efed;
  --color-text-primary: #172c2a;
  --color-text-secondary: #637572;
  --color-text-inverted: #ffffff;
  --color-border: #d9e2e0;
  --color-border-strong: #9badaa;
  --color-accent: #176b61;
  --color-accent-hover: #11574f;
  --color-accent-soft: #e3f1ee;
  --color-danger: #b33f35;
  --color-danger-hover: #923129;
  --color-danger-soft: #f8e9e7;
  --color-warning: #8c6115;
  --color-warning-soft: #f8f0dc;
  --color-success: #23765a;
  --color-success-soft: #e2f2eb;
  --color-info: #315f91;
  --color-info-soft: #e6eef7;
  --focus-ring: 0 0 0 3px rgb(23 107 97 / 24%);
  --shadow-raised: 0 8px 28px rgb(23 60 53 / 8%);
}

[data-theme='dark'] {
  color-scheme: dark;
  --color-background: #101514;
  --color-surface: #171e1d;
  --color-surface-muted: #202927;
  --color-surface-hover: #293431;
  --color-text-primary: #edf4f2;
  --color-text-secondary: #9baca8;
  --color-text-inverted: #10201e;
  --color-border: #2c3936;
  --color-border-strong: #536762;
  --color-accent: #69b9aa;
  --color-accent-hover: #85cabd;
  --color-accent-soft: #1b3933;
  --color-danger: #ee8176;
  --color-danger-hover: #f29a91;
  --color-danger-soft: #3c2220;
  --color-warning: #e4b65c;
  --color-warning-soft: #372e1d;
  --color-success: #63bc98;
  --color-success-soft: #1c362c;
  --color-info: #86add8;
  --color-info-soft: #1e3044;
  --focus-ring: 0 0 0 3px rgb(105 185 170 / 32%);
  --shadow-raised: 0 8px 28px rgb(0 0 0 / 30%);
}

@media (prefers-reduced-motion: reduce) {
  :root { --motion-fast: 0ms; }
}
```

- [ ] **Step 6: Wire the theme before React render**

Rename `index.css` to `index.scss` without changing selectors yet. In `main.tsx`, import Inter weights, tokens, and SCSS; call `applyInitialTheme()` before `createRoot`; wrap the existing error boundary/router with `ThemeProvider`:

```tsx
import '@fontsource/inter/400.css'
import '@fontsource/inter/500.css'
import '@fontsource/inter/600.css'
import './ui/tokens.scss'
import './index.scss'
import { applyInitialTheme } from './ui/theme/theme'
import { ThemeProvider } from './ui/theme/ThemeProvider'

applyInitialTheme()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <ErrorBoundary>
        <BrowserRouter><App /></BrowserRouter>
      </ErrorBoundary>
    </ThemeProvider>
  </StrictMode>,
)
```

Retain the existing React, router, App, and ErrorBoundary imports.

- [ ] **Step 7: Verify GREEN and commit**

Run:

```bash
cd client/web
npm run test -- src/ui/theme/ThemeProvider.test.tsx
npm run lint
npm run build
```

Expected: theme tests pass, lint exits 0, production build exits 0.

Commit:

```bash
git add client/web/package.json client/web/package-lock.json client/web/src/main.tsx client/web/src/index.scss client/web/src/ui/tokens.scss client/web/src/ui/theme
git commit -m "feat(frontend): add design tokens and theme state"
```

---

### Task 2: Typography and loading primitives

**Files:**

- Create: `client/web/src/ui/components/Text/Text.tsx`
- Create: `client/web/src/ui/components/Text/Text.scss`
- Test: `client/web/src/ui/components/Text/Text.test.tsx`
- Create: `client/web/src/ui/components/Heading/Heading.tsx`
- Create: `client/web/src/ui/components/Heading/Heading.scss`
- Test: `client/web/src/ui/components/Heading/Heading.test.tsx`
- Create: `client/web/src/ui/components/Spinner/Spinner.tsx`
- Create: `client/web/src/ui/components/Spinner/Spinner.scss`
- Test: `client/web/src/ui/components/Spinner/Spinner.test.tsx`
- Create: `client/web/src/ui/index.ts`

**Interfaces:**

- Produces: `Text({ as, variant, tone, ...nativeProps })`.
- Produces: `Heading({ as, size, ...nativeProps })` with semantic level independent from visual size.
- Produces: `Spinner({ label?, size? })`; unlabeled is decorative, labeled has `role="status"`.

- [ ] **Step 1: Write failing semantic tests**

Test that Text can render a `span`, Heading can render an `h2` with visual size 1, an unlabeled Spinner is hidden from accessibility, and a labeled Spinner is a named status:

```tsx
render(<Text as="span">Secondary copy</Text>)
expect(screen.getByText('Secondary copy').tagName).toBe('SPAN')

render(<Heading as="h2" size="1">Account</Heading>)
expect(screen.getByRole('heading', { level: 2, name: 'Account' })).toBeVisible()

const { rerender } = render(<Spinner />)
expect(screen.queryByRole('status')).not.toBeInTheDocument()
rerender(<Spinner label="Đang tải" />)
expect(screen.getByRole('status', { name: 'Đang tải' })).toBeVisible()
```

- [ ] **Step 2: Run and verify RED**

Run `npm run test -- src/ui/components/Text src/ui/components/Heading src/ui/components/Spinner`.

Expected: FAIL because all three modules are missing.

- [ ] **Step 3: Implement minimal native primitives**

Use these public prop shapes:

```tsx
type TextProps = React.HTMLAttributes<HTMLElement> & {
  as?: 'p' | 'span' | 'div'
  variant?: 'body' | 'label' | 'caption'
  tone?: 'primary' | 'secondary' | 'danger'
}

type HeadingProps = React.HTMLAttributes<HTMLHeadingElement> & {
  as?: 'h1' | 'h2' | 'h3'
  size?: '1' | '2' | '3'
}

type SpinnerProps = {
  label?: string
  size?: 'small' | 'medium'
}
```

Build BEM classes explicitly:

```tsx
const className = [
  'td-text',
  `td-text--${variant}`,
  `td-text--${tone}`,
  providedClassName,
].filter(Boolean).join(' ')
```

Heading uses `td-heading`, `td-heading--${size}`. Spinner uses `td-spinner`, `td-spinner--${size}` and an inner `td-spinner__circle`; set `aria-hidden="true"` when no label, otherwise set `role="status"` and `aria-label={label}`.

Write SCSS beneath each block using only the token surface from Task 1. Spinner animation uses `var(--motion-fast)` as part of a readable duration such as `calc(var(--motion-fast) * 6)`.

- [ ] **Step 4: Export and verify GREEN**

Export `Text`, `Heading`, and `Spinner` plus their prop types from `src/ui/index.ts`.

Run:

```bash
cd client/web
npm run test -- src/ui/components/Text src/ui/components/Heading src/ui/components/Spinner
npm run lint
```

Expected: all new tests and lint pass.

- [ ] **Step 5: Commit**

```bash
git add client/web/src/ui
git commit -m "feat(frontend): add typography and spinner primitives"
```

---

### Task 3: Button and IconButton

**Files:**

- Create: `client/web/src/ui/components/Button/Button.tsx`
- Create: `client/web/src/ui/components/Button/Button.scss`
- Test: `client/web/src/ui/components/Button/Button.test.tsx`
- Create: `client/web/src/ui/components/IconButton/IconButton.tsx`
- Create: `client/web/src/ui/components/IconButton/IconButton.scss`
- Test: `client/web/src/ui/components/IconButton/IconButton.test.tsx`
- Modify: `client/web/src/ui/index.ts`

**Interfaces:**

- Produces: native Button with `variant`, `size`, `loading`, `fullWidth`, `startIcon`, and `endIcon`.
- Produces: IconButton with required string `aria-label`, `variant`, `size`, and `loading`.
- Consumes: `Spinner` from Task 2.

- [ ] **Step 1: Write failing interaction tests**

Cover click, loading, dimensions-preserving content, and accessible icon naming:

```tsx
it('prevents activation while loading', async () => {
  const onClick = vi.fn()
  const user = userEvent.setup()
  render(<Button loading onClick={onClick}>Save</Button>)
  const button = screen.getByRole('button', { name: 'Save' })
  expect(button).toBeDisabled()
  expect(button).toHaveAttribute('aria-busy', 'true')
  await user.click(button)
  expect(onClick).not.toHaveBeenCalled()
})

it('requires and exposes the icon button name', () => {
  render(<IconButton aria-label="Đóng"><IconX /></IconButton>)
  expect(screen.getByRole('button', { name: 'Đóng' })).toBeVisible()
})
```

- [ ] **Step 2: Run and verify RED**

Run `npm run test -- src/ui/components/Button src/ui/components/IconButton`.

Expected: FAIL because the components do not exist.

- [ ] **Step 3: Implement Button**

Use this prop surface:

```tsx
type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
  size?: 'small' | 'medium'
  loading?: boolean
  fullWidth?: boolean
  startIcon?: React.ReactNode
  endIcon?: React.ReactNode
}
```

Default to `primary` and `medium`. Add BEM modifiers for variant, size, loading, and full width. Set `disabled={disabled || loading}` and `aria-busy={loading || undefined}`. Keep the label in the DOM and visually hide the content while a centered Spinner is shown so the accessible name and dimensions remain stable. Mark decorative icons and Spinner `aria-hidden`.

SCSS must implement the four variants, two heights, focus ring, hover, disabled opacity/cursor, and a positioned loader using the semantic tokens.

- [ ] **Step 4: Implement IconButton**

Use:

```tsx
type IconButtonProps = Omit<
  React.ButtonHTMLAttributes<HTMLButtonElement>,
  'aria-label'
> & {
  'aria-label': string
  variant?: ButtonProps['variant']
  size?: ButtonProps['size']
  loading?: boolean
}
```

Keep IconButton as a real `<button type="button">`; do not wrap Button if the wrapper adds branching. Use a square control token, the same color semantics, and `td-icon-button` BEM classes.

- [ ] **Step 5: Verify GREEN and commit**

Run:

```bash
cd client/web
npm run test -- src/ui/components/Button src/ui/components/IconButton
npm run lint
```

Expected: tests and lint pass.

Commit:

```bash
git add client/web/src/ui/components/Button client/web/src/ui/components/IconButton client/web/src/ui/index.ts
git commit -m "feat(frontend): add accessible button components"
```

---

### Task 4: FormField, Input, and PasswordInput

**Files:**

- Create: `client/web/src/ui/components/FormField/FormField.tsx`
- Create: `client/web/src/ui/components/FormField/FormField.scss`
- Test: `client/web/src/ui/components/FormField/FormField.test.tsx`
- Create: `client/web/src/ui/components/Input/Input.tsx`
- Create: `client/web/src/ui/components/Input/Input.scss`
- Test: `client/web/src/ui/components/Input/Input.test.tsx`
- Create: `client/web/src/ui/components/PasswordInput/PasswordInput.tsx`
- Create: `client/web/src/ui/components/PasswordInput/PasswordInput.scss`
- Test: `client/web/src/ui/components/PasswordInput/PasswordInput.test.tsx`
- Modify: `client/web/src/ui/index.ts`

**Interfaces:**

- Produces: `FieldControlProps` containing `id`, `aria-describedby`, and `aria-invalid`.
- Produces: `FormField({ label, description?, error?, required?, children(fieldProps) })`.
- Produces: native `Input` and composed `PasswordInput`.
- Consumes: `IconButton` from Task 3 and Tabler `IconEye`/`IconEyeOff`.

- [ ] **Step 1: Write failing relationship and reveal tests**

```tsx
it('connects the label, description, and error to the control', () => {
  render(
    <FormField label="Email" description="Use your work email" error="Required">
      {(props) => <Input {...props} />}
    </FormField>,
  )
  const input = screen.getByRole('textbox', { name: 'Email' })
  expect(input).toHaveAccessibleDescription('Use your work email Required')
  expect(input).toHaveAttribute('aria-invalid', 'true')
})

it('reveals the existing password without losing its value', async () => {
  const user = userEvent.setup()
  render(<PasswordInput aria-label="Mật khẩu" />)
  const input = screen.getByLabelText('Mật khẩu')
  await user.type(input, 'secret')
  await user.click(screen.getByRole('button', { name: 'Hiện mật khẩu' }))
  expect(input).toHaveAttribute('type', 'text')
  expect(input).toHaveValue('secret')
  expect(screen.getByRole('button', { name: 'Ẩn mật khẩu' })).toBeVisible()
})
```

- [ ] **Step 2: Run and verify RED**

Run `npm run test -- src/ui/components/FormField src/ui/components/Input src/ui/components/PasswordInput`.

Expected: FAIL because the form components do not exist.

- [ ] **Step 3: Implement FormField with a render prop**

Use `useId()` and this exact public shape:

```tsx
export type FieldControlProps = {
  id: string
  'aria-describedby'?: string
  'aria-invalid'?: true
}

type FormFieldProps = {
  label: string
  description?: string
  error?: string
  required?: boolean
  children: (props: FieldControlProps) => React.ReactNode
}
```

Render `td-form-field`, `td-form-field__label`, optional description, control, and error. Join description and error IDs in display order. Use `role="alert"` only for an error that is present. Render a visible required marker with `aria-hidden`; include “bắt buộc” in screen-reader-only text.

- [ ] **Step 4: Implement Input and PasswordInput**

Input forwards `InputHTMLAttributes<HTMLInputElement>`, merges `td-input`, `td-input--invalid`, and caller class names, and relies on native disabled/required/invalid attributes.

PasswordInput uses:

```tsx
type PasswordInputProps = Omit<
  React.InputHTMLAttributes<HTMLInputElement>,
  'type'
> & {
  showPasswordLabel?: string
  hidePasswordLabel?: string
}
```

Default labels to `Hiện mật khẩu` and `Ẩn mật khẩu`. Own only `visible`; render Input with `type={visible ? 'text' : 'password'}` and an IconButton inside `td-password-input__action`. Preserve every caller-provided native prop and value/defaultValue behavior.

- [ ] **Step 5: Verify GREEN and commit**

Run:

```bash
cd client/web
npm run test -- src/ui/components/FormField src/ui/components/Input src/ui/components/PasswordInput
npm run lint
```

Expected: tests and lint pass.

Commit:

```bash
git add client/web/src/ui/components/FormField client/web/src/ui/components/Input client/web/src/ui/components/PasswordInput client/web/src/ui/index.ts
git commit -m "feat(frontend): add shared form controls"
```

---

### Task 5: Checkbox and Alert

**Files:**

- Create: `client/web/src/ui/components/Checkbox/Checkbox.tsx`
- Create: `client/web/src/ui/components/Checkbox/Checkbox.scss`
- Test: `client/web/src/ui/components/Checkbox/Checkbox.test.tsx`
- Create: `client/web/src/ui/components/Alert/Alert.tsx`
- Create: `client/web/src/ui/components/Alert/Alert.scss`
- Test: `client/web/src/ui/components/Alert/Alert.test.tsx`
- Modify: `client/web/src/ui/index.ts`

**Interfaces:**

- Produces: controlled Checkbox backed by Base UI.
- Produces: Alert with visual `variant` independent from optional live-region `role`.

- [ ] **Step 1: Write failing controlled and semantics tests**

```tsx
it('reports a keyboard toggle without owning the checked state', async () => {
  const onCheckedChange = vi.fn()
  const user = userEvent.setup()
  render(
    <Checkbox checked={false} onCheckedChange={onCheckedChange} label="Remember me" />,
  )
  await user.tab()
  await user.keyboard('[Space]')
  expect(onCheckedChange).toHaveBeenCalledWith(true)
})

it('does not create a live region unless the caller asks for one', () => {
  const { rerender } = render(<Alert variant="error">Invalid</Alert>)
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  rerender(<Alert variant="error" role="alert">Invalid</Alert>)
  expect(screen.getByRole('alert')).toHaveTextContent('Invalid')
})
```

- [ ] **Step 2: Run and verify RED**

Run `npm run test -- src/ui/components/Checkbox src/ui/components/Alert`.

Expected: FAIL because the components do not exist.

- [ ] **Step 3: Implement Checkbox with Base UI**

Use `Checkbox.Root` and `Checkbox.Indicator` from `@base-ui/react/checkbox`, `IconCheck` from Tabler, and this prop shape:

```tsx
type CheckboxProps = {
  checked: boolean
  onCheckedChange: (checked: boolean) => void
  label: string
  disabled?: boolean
  name?: string
  value?: string
}
```

Render one `<label className="td-checkbox">` containing the Base UI root and visible label. Apply disabled styling from the native/Base UI state selector offered by the installed version; do not mirror `checked` into React state.

- [ ] **Step 4: Implement Alert**

Use native `<div>` and:

```tsx
type AlertProps = React.HTMLAttributes<HTMLDivElement> & {
  variant?: 'info' | 'success' | 'warning' | 'error'
  title?: string
}
```

Default to `info`; use `td-alert--${variant}`. Render title and children in `td-alert__title` and `td-alert__content`. Pass `role` through unchanged instead of deriving announcements from color.

- [ ] **Step 5: Verify GREEN and commit**

Run:

```bash
cd client/web
npm run test -- src/ui/components/Checkbox src/ui/components/Alert
npm run lint
```

Expected: tests and lint pass.

Commit:

```bash
git add client/web/src/ui/components/Checkbox client/web/src/ui/components/Alert client/web/src/ui/index.ts
git commit -m "feat(frontend): add checkbox and alert components"
```

---

### Task 6: AuthLayout

**Files:**

- Create: `client/web/src/ui/layouts/AuthLayout/AuthLayout.tsx`
- Create: `client/web/src/ui/layouts/AuthLayout/AuthLayout.scss`
- Test: `client/web/src/ui/layouts/AuthLayout/AuthLayout.test.tsx`
- Modify: `client/web/src/ui/index.ts`

**Interfaces:**

- Produces: presentation-only `AuthLayout({ title, description?, children, footer? })`.
- Consumes: Heading and Text from Task 2.

- [ ] **Step 1: Write the failing layout test**

```tsx
it('provides named authentication content and optional footer', () => {
  render(
    <AuthLayout
      title="Đăng nhập"
      description="Tiếp tục với tài khoản TempoDesk"
      footer={<a href="/privacy">Quyền riêng tư</a>}
    >
      <form aria-label="Đăng nhập" />
    </AuthLayout>,
  )
  expect(screen.getByRole('heading', { name: 'Đăng nhập' })).toBeVisible()
  expect(screen.getByRole('form', { name: 'Đăng nhập' })).toBeVisible()
  expect(screen.getByRole('link', { name: 'Quyền riêng tư' })).toBeVisible()
})
```

- [ ] **Step 2: Run and verify RED**

Run `npm run test -- src/ui/layouts/AuthLayout`.

Expected: FAIL because AuthLayout does not exist.

- [ ] **Step 3: Implement the presentation shell**

Use:

```tsx
type AuthLayoutProps = React.PropsWithChildren<{
  title: string
  description?: string
  footer?: React.ReactNode
}>
```

Render a full viewport `td-auth-layout`, a brand link to `/` labeled `TempoDesk trang chủ`, a constrained `td-auth-layout__card`, header, content, and optional footer. Do not import the API client, router hooks, or authentication types. Desktop uses a raised card up to 26rem; at 640px and below the card becomes edge-to-edge with safe padding and no shadow.

- [ ] **Step 4: Verify GREEN and commit**

Run:

```bash
cd client/web
npm run test -- src/ui/layouts/AuthLayout
npm run lint
npm run build
```

Expected: test, lint, and build pass.

Commit:

```bash
git add client/web/src/ui/layouts/AuthLayout client/web/src/ui/index.ts
git commit -m "feat(frontend): add responsive authentication layout"
```

---

### Task 7: UI Kit, routing, and existing-shell migration

**Files:**

- Create: `client/web/src/app/UiKitPage.tsx`
- Create: `client/web/src/app/UiKitPage.scss`
- Test: `client/web/src/app/UiKitPage.test.tsx`
- Modify: `client/web/src/App.tsx`
- Modify: `client/web/src/App.test.tsx`
- Modify: `client/web/src/index.scss`
- Modify: `client/web/src/app/HealthStatus.tsx`
- Modify: `client/web/src/app/ErrorBoundary.tsx`
- Modify: `client/web/vite.config.ts`

**Interfaces:**

- Consumes: every public UI export from Tasks 1–6.
- Produces: a development-only `/ui-kit` route and no production route.

- [ ] **Step 1: Write failing UI Kit and route tests**

The page test renders `UiKitPage` inside `ThemeProvider` and verifies representative states by accessible name:

```tsx
it('shows supported components and changes theme preference', async () => {
  const user = userEvent.setup()
  render(<ThemeProvider><UiKitPage /></ThemeProvider>)
  expect(screen.getByRole('heading', { name: 'TempoDesk UI Kit' })).toBeVisible()
  expect(screen.getByRole('button', { name: 'Đang lưu' })).toBeDisabled()
  expect(screen.getByRole('textbox', { name: 'Email lỗi' })).toBeInvalid()
  await user.click(screen.getByRole('button', { name: 'Dark' }))
  expect(document.documentElement).toHaveAttribute('data-theme', 'dark')
})
```

Add an App test using `MemoryRouter initialEntries={['/ui-kit']}` and expect the UI Kit heading in Vitest's development mode.

- [ ] **Step 2: Run and verify RED**

Run:

```bash
cd client/web
npm run test -- src/app/UiKitPage.test.tsx src/App.test.tsx
```

Expected: FAIL because UiKitPage and its route do not exist.

- [ ] **Step 3: Build the UI Kit page**

Create sections for:

- theme selector with `System`, `Light`, and `Dark` buttons backed by `useTheme`;
- color surfaces and semantic feedback roles;
- Text and Heading variants;
- Button and IconButton variants, disabled, loading, icons, and full width;
- valid, disabled, and error Input/FormField states;
- PasswordInput and checked/unchecked Checkbox examples with local demo state;
- Alert variants and Spinner sizes;
- a bounded AuthLayout preview.

Use one `td-ui-kit` BEM block. Local state is allowed only for the interactive demos. The page must not call an API or import authentication services.

- [ ] **Step 4: Add the development-only route**

In `App.tsx`, add:

```tsx
{import.meta.env.DEV && <Route path="/ui-kit" element={<UiKitPage />} />}
```

Extract a local `AppShell` component inside `App.tsx` and wrap only the existing landing and catch-all routes. Render the full-screen UI Kit route outside that shell; do not create a reusable layout abstraction until another production page needs it.

- [ ] **Step 5: Migrate touched application selectors to tokens and BEM**

Rename the existing global classes without changing behavior:

- `.app-shell` → `.td-app-shell` and use `__header`, `__main`, `__footer`.
- `.skip-link` → `.td-skip-link`.
- `.brand` and `.brand-mark` → `.td-brand` and `.td-brand__mark`.
- `.health`, state class, and `.status-dot` → `.td-health-status`, `--loading|healthy|unavailable|malformed`, and `__dot`.
- `.fallback` → `.td-error-fallback`.

Replace current hard-coded background, text, border, radius, focus, and shadow values with Task 1 tokens. Preserve the responsive layout and Vietnamese copy.

- [ ] **Step 6: Include UI code in coverage**

Change Vite coverage include to:

```ts
include: [
  'src/lib/**',
  'src/config/**',
  'src/app/**',
  'src/ui/**',
  'src/App.tsx',
],
```

- [ ] **Step 7: Verify GREEN and commit**

Run:

```bash
cd client/web
npm run format
npm run lint
npm run test -- --coverage
npm run build
npm audit --audit-level=high
```

Expected: formatting succeeds; lint exits 0; all tests pass with coverage output; production build succeeds; audit finds no high/critical vulnerability.

Commit:

```bash
git add client/web
git commit -m "feat(frontend): add design system UI Kit"
```

---

### Task 8: Visual and final regression verification

**Files:**

- Modify only files proven necessary by verification; do not add features.

- [ ] **Step 1: Start the development server**

Run:

```bash
cd client/web
npm run dev
```

Expected: Vite reports a local URL and no compile error.

- [ ] **Step 2: Verify UI Kit visually**

At `/ui-kit`, inspect:

- light, dark, and system preferences;
- desktop width around 1280px and mobile width around 390px;
- visible focus while tabbing through every interactive control;
- disabled controls do not activate;
- password reveal keeps its value;
- errors remain connected and readable;
- AuthLayout has no horizontal overflow;
- reduced-motion mode removes spinner animation duration.

Expected: every supported variant is visible, readable, keyboard operable, and free of clipping or overflow.

- [ ] **Step 3: Run the complete frontend CI sequence again**

Run:

```bash
cd client/web
npm ci
npm run format:check
npm run lint
npm run test -- --coverage --reporter=default --reporter=junit --outputFile=../../artifacts/frontend/tests.xml
npm run build
npm audit --audit-level=high
```

Expected: every command exits 0; all tests pass; build emits the production bundle; no high/critical audit finding.

- [ ] **Step 4: Inspect the final diff and commit verification fixes only**

Run:

```bash
git diff --check
git status --short
```

If visual or regression verification required a code correction, rerun the directly affected test plus the complete sequence above, then commit only that correction:

```bash
git add client/web
git commit -m "fix(frontend): resolve design system verification findings"
```

If no correction was required, do not create an empty commit.
