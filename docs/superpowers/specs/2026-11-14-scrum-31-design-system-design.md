# SCRUM-31 frontend design system design

## Goal

Create the shared UI foundation for TempoDesk Sprint 1. The design language takes selected structural and visual cues from Twenty while keeping TempoDesk's React/Vite architecture, teal identity, and smaller scope.

This ticket supplies tokens, reusable components, an authentication layout, and a development UI Kit. Registration, login, password recovery, API integration, and authentication business state remain outside SCRUM-31.

## Reference and license boundary

Twenty is a reference for semantic tokens, compact component APIs, neutral surfaces, subtle borders, consistent interaction states, and colocated behavior tests.

`packages/twenty-ui` is MIT licensed. TempoDesk may study or selectively reuse code from that package only when reuse is materially smaller than an original implementation and the MIT notice is preserved as required. `packages/twenty-front`, including its authentication layout, is AGPL licensed; TempoDesk will not copy that source. Its layout may be observed and independently reimplemented.

TempoDesk will not depend on the `twenty-ui` package and will not adopt Twenty's Nx, Linaria, Jotai, generated token pipeline, or monorepo structure.

## Dependencies

Add only:

- `@base-ui/react` for interactive primitives whose keyboard, focus, or form behavior would otherwise be reimplemented;
- `@tabler/icons-react` for interface icons;
- `@fontsource/inter` so Inter is bundled rather than loaded from an external font service;
- `@testing-library/user-event` for interaction tests;
- `sass` as the build-time stylesheet compiler.

Native HTML remains the first choice for text, headings, buttons, inputs, alerts, and spinners. Checkbox is the only initial component built on Base UI.

## File structure

```text
client/web/src/ui/
  components/
    Alert/
    Button/
    Checkbox/
    FormField/
    Heading/
    IconButton/
    Input/
    PasswordInput/
    Spinner/
    Text/
  layouts/
    AuthLayout/
  theme/
    ThemeProvider.tsx
    theme.ts
  tokens.scss
  index.ts
client/web/src/app/UiKitPage.tsx
```

Each reusable component owns its `.tsx`, global `.scss`, and behavior test. Component files import their stylesheet directly so Vite includes only styles reached by the application. Do not add one-file barrels inside every folder; `ui/index.ts` is the single public export surface.

The existing application stylesheet becomes `index.scss`. Shared resets and page-shell rules remain there; reusable component styles stay beside their components.

## SCSS and BEM

All component styles are global SCSS using BEM. Every block is prefixed with `td-` to avoid collisions with application and third-party styles:

```scss
.td-button {
  &__icon { /* element */ }
  &--primary { /* visual variant */ }
  &--loading { /* component-owned state */ }
}
```

Use one BEM block per reusable component. Elements use `__`; visual variants and component-owned states use `--`. Native interaction states continue to use semantic selectors such as `:hover`, `:focus-visible`, and `:disabled` rather than duplicate classes.

Nesting stays beneath the owning block and should not exceed the level needed to express an element or modifier. A component must not reach into another component's BEM elements. JSX builds class names explicitly from the base block and applicable modifiers; no class-name dependency is added for this ticket.

## Design tokens and visual language

Tokens are semantic CSS custom properties authored in SCSS rather than a generated TypeScript token system. SCSS organizes the theme declarations, while CSS custom properties provide runtime light/dark switching. The root token file defines:

- neutral backgrounds, surfaces, borders, and text roles;
- TempoDesk teal accent roles and danger, warning, success, and information roles;
- Inter font family, type sizes, line heights, and weights;
- a 4px spacing scale;
- small, medium, and large radii;
- focus ring, shadows, control heights, icon sizes, and short motion durations.

Light tokens live on `:root` and `[data-theme='light']`; dark overrides live on `[data-theme='dark']`. Components consume semantic roles such as `--color-text-primary` and `--color-surface-raised`, never palette steps or raw color values.

The visual result should feel as restrained and compact as Twenty but remain recognizably TempoDesk through its teal accent and product copy. Pixel-for-pixel reproduction is not a goal.

## Theme state

Theme preference is the only shared state introduced by this ticket. `ThemeProvider` owns `light | dark | system`, persists the preference in `localStorage`, observes `prefers-color-scheme` only while `system` is selected, and writes the resolved theme to `document.documentElement.dataset.theme`. A small synchronous bootstrap called from `main.tsx` resolves the stored or system preference before React renders to avoid a visible wrong-theme flash.

The context exposes the preference, resolved theme, and setter. It does not expose the token object or computed CSS values.

No Redux, Jotai, Zustand, or other global state library is added.

## Component state rules

- Form values and validation are controlled by callers.
- `PasswordInput` alone owns its reveal/conceal state because it is private presentation behavior.
- `Button` receives `loading`; it disables activation, exposes busy state, and never starts async work itself.
- `FormField` composes label, description, control, and error IDs but does not run validation.
- `Checkbox` supports controlled use and delegates keyboard and form semantics to Base UI.
- Derived visual state is computed during render rather than copied into state.
- Effects synchronize only with browser systems such as media queries and storage.

## Components

### Text and Heading

Provide semantic element selection and a small token-backed variant set. Defaults produce valid HTML without requiring consumers to configure typography. Visual variants do not silently change heading level.

### Button and IconButton

Button supports `primary`, `secondary`, `ghost`, and `danger` variants in `small` and `medium` sizes, plus hover, focus-visible, disabled, full-width, and loading states. Loading preserves the button dimensions. IconButton supports the same colors and sizes, requires an accessible name, and does not infer one from its icon.

### Input, PasswordInput, and FormField

Input forwards native input props and supports invalid and disabled presentation. FormField generates and connects stable IDs for label, optional description, and error. PasswordInput composes these primitives and provides a keyboard-operable, named reveal button without changing the entered value.

### Checkbox

Use Base UI for checked, unchecked, focus-visible, disabled, and form behavior. The visible label remains a standard label associated with the control.

### Alert and Spinner

Alert supports information, success, warning, and error roles with appropriate semantics chosen by the caller. Spinner is decorative by default; when it represents standalone progress, the caller supplies an accessible label.

## AuthLayout

AuthLayout provides a full-viewport neutral background, TempoDesk brand area, constrained content card, and optional footer slot. It is vertically comfortable on desktop and becomes edge-to-edge with safe padding on small screens. It consumes tokens and has no authentication logic, routing assumptions, or API calls.

## UI Kit

Add a development-only `/ui-kit` route. It displays token roles, typography, every component variant, and supported interaction states in both themes. The route is omitted from production routing and is not a Storybook replacement.

The page also provides the theme preference control needed to verify `light`, `dark`, and `system` behavior.

## Accessibility

- Every control is keyboard operable and has a visible focus indicator.
- Labels, descriptions, and errors are programmatically connected.
- Icon-only buttons require an accessible name.
- Loading and invalid states expose appropriate native or ARIA state without duplicating announcements.
- Light and dark token pairs maintain readable contrast.
- Motion respects `prefers-reduced-motion`.

## Testing and verification

Write behavior tests before each non-trivial component implementation. Cover:

- theme preference persistence and system-theme changes;
- Button disabled/loading behavior;
- IconButton accessible naming;
- Input and FormField label, description, and error relationships;
- PasswordInput reveal behavior without value loss;
- Checkbox keyboard and controlled behavior;
- AuthLayout rendering and mobile-safe structure;
- UI Kit rendering of supported variants.

Run frontend lint, tests with coverage, production build, and the existing high-severity dependency audit. Verify the UI Kit manually at desktop and mobile widths in light and dark themes.

## Out of scope

- Registration, login, password recovery, and their routes.
- Authentication API calls, validation rules, submit state, or session state.
- Dashboard, navigation shell, tables, modals, menus, notifications, and data-display components.
- Storybook, a generated token pipeline, a published component package, or a global state library.
- Pixel-for-pixel cloning of Twenty or copying code from `twenty-front`.
