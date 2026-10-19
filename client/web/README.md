# TempoDesk Web

Requires Node.js 22.12 or newer. The app uses React, strict TypeScript, Vite and React Router in declarative mode.

```sh
npm ci
cp .env.example .env.local
npm run dev
npm run lint
npm run test
npm run build
npm run format:check
```

Run commands from `client/web`. Vite serves http://localhost:5173. The landing page checks `/api/health/ready`; start the API at http://localhost:5023 or use Docker Compose from the repository root. A missing API renders an unavailable status, never a blank page.

## Configuration

`VITE_API_BASE_URL` defaults to `/api`; `VITE_SIGNALR_BASE_URL` defaults to `/hubs`. Both accept HTTP(S) URLs or safe root-relative paths. Configuration is validated before rendering the landing page. Values in `VITE_*` are public build-time configuration: never put credentials in them. Only allowlisted build metadata is displayed.

For host development, `API_PROXY_TARGET` defaults to `http://localhost:5023`. The Vite server strips `/api` before proxying to the backend and forwards `/hubs` with WebSocket support. It is server-only configuration. No SignalR connection is implemented in Sprint 0.

The Dockerfile builds from the repository root and serves static files using non-root nginx on port 8080. Nginx forwards `/api/` to `http://api:8080/` and `/hubs` to the same API service. Client URLs are compiled into the image; changing them requires rebuilding. Runtime Compose values are not injected into browser code. Nginx returns the application shell for client routes, including the not-found page.

## Boundaries and checks

`src/app` owns shell infrastructure, `src/config` validates public environment values and `src/lib/api-client.ts` is the only HTTP boundary. Create business modules under `src/features` when their implementation starts. Feature code must use the API client; ESLint rejects direct `fetch` calls. The request-header callback reserves future token injection without implementing authentication.

API requests support JSON, empty responses, caller abort and a 10-second default timeout. `ApiError` preserves status, safe Problem Details detail, trace ID and validation fields, with a `kind` discriminator for HTTP, network, cancellation, timeout and malformed JSON failures. Treat API response types as untrusted: the readiness query validates its response before displaying health. No automatic retries are performed.

```sh
npm run test -- --coverage --reporter=default --reporter=junit --outputFile=../../artifacts/frontend/tests.xml
```

Tests run without backend services; coverage and JUnit reports go under root `artifacts/frontend`. Accessibility includes semantic landmarks, a skip link, keyboard focus and a live health status. No business routes, authentication or business feature UI are included.

Toolchain references: [Vite requirements](https://vite.dev/guide/), [React Router declarative routing](https://reactrouter.com/start/declarative/routing), [Vitest guide](https://vitest.dev/guide/).
