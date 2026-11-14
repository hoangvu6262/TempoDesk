# Auth Layout Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Group the TempoDesk brand and authentication card into one balanced, responsive unit.

**Architecture:** Keep the existing `AuthLayout` markup and correct its grid behavior in the shared SCSS. A single centered grid column will serve every authentication route without page-specific layout overrides.

**Tech Stack:** React 19, TypeScript, SCSS, Vite

## Global Constraints

- Keep all existing English copy and authentication behavior unchanged.
- Do not change routes, API calls, validation, or component contracts.
- Do not create or restore test files.
- Keep the current border, rounded corners, surface color, and subtle shadow.

---

### Task 1: Balance the shared authentication layout

**Files:**
- Modify: `client/web/src/ui/layouts/AuthLayout/AuthLayout.scss:1-67`

**Interfaces:**
- Consumes: the existing `.td-auth-layout` BEM structure rendered by `AuthLayout`.
- Produces: the same class contract with centered desktop behavior and responsive mobile sizing.

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

- [ ] **Step 3: Format and run static verification**

Run:

```bash
npm --prefix client/web run format
npm --prefix client/web run lint
npm --prefix client/web run format:check
npm --prefix client/web run build
```

Expected: every command exits with status 0. Do not run or add tests.

- [ ] **Step 4: Inspect the login and registration layouts**

Start the app with `npm --prefix client/web run dev`, then inspect `/login` and
`/register` at approximately 1440×900 and 390×844. Confirm the brand sits 24px
above the card, both elements share the same horizontal center, the card retains
its border radius on mobile, and no horizontal scrolling appears.

- [ ] **Step 5: Commit the layout change**

```bash
git add client/web/src/ui/layouts/AuthLayout/AuthLayout.scss
git commit -m "style(auth): balance authentication layout"
```
