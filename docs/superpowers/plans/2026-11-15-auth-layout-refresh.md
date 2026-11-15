# Auth Layout Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Group the TempoDesk brand and authentication card into one balanced, responsive unit with slightly larger auth typography.

**Architecture:** Keep the existing `AuthLayout` markup and correct its grid behavior in the shared SCSS. A single centered grid column serves every authentication route, while local typography token overrides enlarge auth content without changing the global design system.

**Tech Stack:** React 19, TypeScript, SCSS, Vite

## Global Constraints

- Keep all existing English copy and authentication behavior unchanged.
- Do not change routes, API calls, validation, or component contracts.
- Do not create or restore test files.
- Keep the current border, rounded corners, surface color, and subtle shadow.
- Keep the 28px authentication page heading unchanged.
- Do not change global typography or add component variants.

---

### Task 1: Balance and enlarge the shared authentication layout

**Files:**
- Modify: `client/web/src/ui/layouts/AuthLayout/AuthLayout.scss:1-67`

**Interfaces:**
- Consumes: the existing `.td-auth-layout` BEM structure rendered by `AuthLayout`.
- Produces: the same class contract with centered desktop behavior, responsive mobile sizing, and local auth typography overrides.

- [ ] **Step 1: Replace the stretched two-row grid with one centered column**

Update the root and card rules to:

```scss
.td-auth-layout {
  display: grid;
  min-height: 100vh;
  min-height: 100dvh;
  grid-template-columns: minmax(0, 27rem);
  align-content: safe center;
  justify-content: center;
  gap: var(--space-6);
  padding: var(--space-8) var(--space-4);
  background: var(--color-background);

  // Keep the existing brand and brand-mark declarations unchanged.

  &__card {
    width: 100%;
    border: 1px solid var(--color-border);
    border-radius: var(--radius-large);
    background: var(--color-surface);
    box-shadow: var(--shadow-raised);
  }
}
```

The fixed grid column keeps the brand and card aligned. `safe center` centers the
group when it fits and avoids clipping the top when a long form exceeds the
viewport.

- [ ] **Step 2: Preserve the card treatment on mobile**

Replace the current mobile rules with:

```scss
@media (max-width: 640px) {
  .td-auth-layout {
    padding: var(--space-6) var(--space-3);
  }

  .td-auth-layout__header,
  .td-auth-layout__content,
  .td-auth-layout__footer {
    padding: var(--space-5) var(--space-4);
  }
}
```

Do not remove the card border, radius, or shadow on narrow screens.

- [ ] **Step 3: Increase typography only inside the authentication layout**

Add the existing typography custom properties to the root rule:

```scss
.td-auth-layout {
  --font-size-caption: 0.8125rem;
  --font-size-body: 0.9375rem;
  --font-size-label: 0.875rem;

  // Existing layout declarations follow.
}
```

This increases captions from 12px to 13px, body/input text from 14px to 15px,
and labels/buttons from 13px to 14px. Do not override
`--font-size-heading-1`.

- [ ] **Step 4: Format and run static verification**

Run:

```bash
npm --prefix client/web run format
npm --prefix client/web run lint
npm --prefix client/web run format:check
npm --prefix client/web run build
```

Expected: every command exits with status 0. Do not run or add tests.

- [ ] **Step 5: Inspect the login and registration layouts**

Start the app with `npm --prefix client/web run dev`, then inspect `/login` and
`/register` at approximately 1440×900 and 390×844. Confirm the brand sits 24px
above the card, both elements share the same horizontal center, the card retains
its border radius on mobile, the supporting text is visibly easier to read, and
no horizontal scrolling appears.

- [ ] **Step 6: Commit the layout change**

```bash
git add client/web/src/ui/layouts/AuthLayout/AuthLayout.scss
git commit -m "style(auth): refine authentication layout"
```
