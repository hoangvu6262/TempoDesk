# English Frontend Error Messages Design

## Goal

Use consistent English for every user-facing frontend error, failure, cancellation, timeout, and invalid-state message while leaving unrelated product copy unchanged.

## Scope

- Translate safe Axios/API error details.
- Translate ErrorBoundary and invalid client configuration fallback content.
- Translate HealthStatus labels and its supporting diagnostic text so one status region does not mix languages.
- Translate UI Kit error examples and their accessible labels.
- Update every affected test assertion.
- Do not change backend Problem Details supplied by the server; valid server-provided `detail` remains displayed as received.
- Do not translate normal landing-page, navigation, button, or design-system demonstration copy outside an error/failure state.

## API Error Constants

Create `client/web/src/constants/errors.ts` with these reusable safe messages:

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

`api-error.ts`, `api-path.ts`, and `api-client.ts` consume these constants. No module duplicates those messages.

## Application Copy

Use these exact translations:

| Location | English copy |
|---|---|
| ErrorBoundary heading | `Something went wrong` |
| ErrorBoundary description | `Reload the page and try again.` |
| ErrorBoundary home link | `Back to home` |
| Invalid configuration heading | `Application configuration is invalid` |
| Invalid configuration description | `Check VITE_API_BASE_URL and VITE_SIGNALR_BASE_URL, then restart the application.` |
| Health loading | `Checking API…` |
| Health ready | `API is ready` |
| Health unavailable | `API is unavailable` |
| Health malformed | `The health response is invalid` |
| Health supporting text | `Check the API connection and database status.` |
| UI Kit invalid field label | `Invalid email` |
| UI Kit invalid field error | `Enter a valid email address.` |
| UI Kit error alert title | `Error` |
| UI Kit error alert content | `Unable to complete the action.` |

## Testing

- API boundary tests assert the exact English safe messages and continue proving that non-Problem server text is hidden.
- HealthStatus tests assert all English states.
- App tests assert the English ErrorBoundary and invalid-configuration messages.
- UI Kit tests find the invalid field using its English accessible name.
- Run format, lint, complete frontend tests with coverage, and production build.

## Out of Scope

- Backend validation or Problem Details localization.
- A general internationalization framework.
- Translating non-error product copy.
- TanStack Query or API transport changes.
