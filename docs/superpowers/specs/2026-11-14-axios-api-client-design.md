# Axios API Client Design

## Goal

Replace TempoDesk Web's hand-written Fetch transport with a small Axios boundary while preserving its security validation, safe error contract, timeout/cancellation behavior, and test coverage.

## Scope

- Add `axios` as the only new runtime dependency.
- Do not add TanStack Query in SCRUM-31. Introduce it with SCRUM-28 when authentication creates meaningful server state, mutations, and cache invalidation.
- Split the current API client into focused files under `client/web/src/lib/api/`.
- Keep `client/web/src/lib/api-client.ts` as a compatibility facade that re-exports the public API.
- Update `HealthStatus` to use the Axios instance returned by `createApiClient`.
- Add a `client/web/src/constants/` folder for stable cross-module policy values, not for every literal in the application.

## Public API

`createApiClient(baseUrl, requestHeaders?)` returns a configured `AxiosInstance`. Callers use normal Axios methods and read response data explicitly:

```ts
const api = createApiClient('/api')
const response = await api.get<HealthResponse>('/health/ready', { signal })
return response.data
```

`requestHeaders` remains a callback so future authentication headers are read at request time instead of captured when the client is created.

`ApiError` remains the application's public transport error with:

- `status`: HTTP status or `0` before a valid HTTP response;
- `detail`: safe user-facing detail;
- optional `traceId` and validation `errors` from valid Problem Details;
- `kind`: `http`, `network`, `timeout`, `cancelled`, or `invalid-response`.

## File Structure

```text
client/web/src/
├── constants/
│   ├── http.ts
│   └── storage.ts
└── lib/
    ├── api/
    │   ├── api-client.ts
    │   ├── api-error.ts
    │   ├── api-path.ts
    │   └── index.ts
    └── api-client.ts
```

- `constants/http.ts` owns the default `10_000` ms timeout and supported JSON/Problem Details media types.
- `constants/storage.ts` owns the `tempodesk.theme` storage key, moved from the theme implementation.
- `api-path.ts` accepts only root-relative paths and rejects absolute/network paths, controls, whitespace, query/fragment characters, percent escapes, backslashes, and dot segments before headers or network work occurs.
- `api-error.ts` owns `ApiError`, Problem Details shape validation, and safe Axios error normalization.
- `api-client.ts` creates Axios instances and installs request/response interceptors.
- `api/index.ts` and the legacy `lib/api-client.ts` expose only `ApiError` and `createApiClient`.

## Request Behavior

The Axios instance uses the configured base URL, default timeout, and JSON response parsing. Its request interceptor:

1. validates the request path before invoking `requestHeaders`;
2. rejects invalid/non-positive timeout values as `invalid-response`;
3. reads and merges dynamic headers without allowing them to escape the configured API base;
4. sends `Accept: application/json, application/problem+json`;
5. relies on Axios to serialize object request bodies passed through `data`.

Axios handles caller `AbortSignal` cancellation and request timeout. The response/error interceptor maps Axios cancellation to `cancelled`, timeout codes to `timeout`, missing responses to `network`, invalid successful JSON to `invalid-response`, and HTTP failures to `http`.

## Response and Error Safety

- Successful JSON responses remain available as `response.data`.
- HTTP 204 remains a successful response with no application data.
- Only responses whose content type includes `application/problem+json` may contribute `detail`, `traceId`, and validation `errors`.
- Non-Problem server bodies are never exposed to users; they receive `Không thể hoàn tất yêu cầu.`.
- Malformed Problem Details fields are ignored rather than trusted.
- Existing Vietnamese safe fallback messages remain unchanged.

## Constants Policy

Use `src/constants` only for stable values shared across modules or representing application policy. Do not move one-off UI copy, component variants, feature-specific API paths, or query keys into a global constants folder. Feature API paths and future TanStack Query keys stay beside their feature.

## Testing and Verification

- Preserve all existing API boundary scenarios: headers/JSON, no content, Problem Details, hidden server text, malformed JSON, unsafe paths, timeout, cancellation, and network errors.
- Test Axios through a deterministic custom adapter assigned to the created instance; do not call the network.
- Update HealthStatus tests only where the transport change requires it; retain all user-visible behavior assertions.
- Run frontend formatting, lint, complete tests with coverage, build, and npm audit at high severity.

## Out of Scope

- TanStack Query, query providers, query keys, cache policies, and DevTools.
- Authentication token refresh or retry interceptors.
- Feature-specific API services for authentication, projects, or tasks.
- Moving every number or string into global constants.
