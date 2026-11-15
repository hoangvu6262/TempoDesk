# Auth Layout Refresh Design

## Goal

Make every authentication screen feel like one balanced unit instead of placing
the TempoDesk brand and form card far apart vertically. Keep the existing English
copy, form behavior, components, and visual identity.

## Layout

- Center one auth container in the viewport.
- Place the TempoDesk brand directly above the card with a 24px gap.
- Keep the card between 400px and 440px wide while allowing it to shrink to the
  available mobile width.
- Use consistent padding across the card header, form content, and footer.
- Preserve the current border, rounded corners, surface color, and subtle shadow.

The layout is shared through `AuthLayout`, so login, registration, verification,
password recovery, reset, and callback screens remain visually consistent.

## Responsive Behavior

Desktop and tablet screens center the complete brand-and-card group both
horizontally and vertically. Small screens keep page padding, retain rounded card
corners, and align toward the top only when the content is taller than the
viewport. No horizontal scrolling is allowed.

## Typography

Increase typography only inside the shared authentication layout so other
application screens and the UI kit remain unchanged:

- Body text and input text increase from 14px to 15px.
- Form labels and button text increase from 13px to 14px.
- Captions, links, helper text, errors, dividers, and footer text increase from
  12px to 13px.
- The 28px page heading remains unchanged.

Implement the sizing by overriding the existing typography custom properties on
`.td-auth-layout`. All child components continue using the shared design-system
tokens without new component variants.

## Scope

The change is limited to shared authentication layout and typography styles. It
does not change authentication logic, routes, API calls, validation, copy, global
typography, or component contracts.

## Verification

No test files will be added. Verify the change with frontend lint, formatting, and
production build, then inspect login and registration at desktop and mobile
viewport sizes.
