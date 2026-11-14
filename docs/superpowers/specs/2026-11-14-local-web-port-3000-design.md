# Local Web Port 3000 Design

## Goal

Use `http://localhost:3000` as TempoDesk's single default frontend address for native development and Docker-host access.

## Scope

- Configure Vite's development server to use port `3000` and fail when that port is already occupied instead of silently selecting another port.
- Change the default `WEB_PORT` in `.env.example` and Docker Compose fallbacks from `5173` to `3000`.
- Change backend development CORS and email frontend URL defaults to `http://localhost:3000`.
- Update active configuration tests and developer documentation to expect port `3000`.
- Keep the web container's internal nginx port at `8080`, the API port at `5023`, and database ports unchanged.

## Configuration Flow

Native development uses Vite on port `3000`. Docker Compose publishes the web container's internal port `8080` to host port `${WEB_PORT:-3000}`. The API allows the same browser origin through its development defaults and Compose environment, while email links use the same frontend base URL.

`WEB_PORT` remains an override for Docker Compose. Native Vite development intentionally uses the fixed port `3000`; developers who need another native port can still pass an explicit Vite CLI argument.

## Verification

- Add a Vite configuration test that asserts `server.port` is `3000` and `server.strictPort` is enabled.
- Update existing backend configuration tests from port `5173` to `3000`.
- Run frontend format, lint, tests, and build.
- Run the affected .NET configuration tests.
- Validate Docker Compose configuration with the updated example environment.

## Out of Scope

- Changing production deployment ports.
- Changing nginx's internal container port `8080`.
- Changing API, PostgreSQL, or mail service ports.
- Rewriting historical implementation plans that mention the old Sprint 0 defaults.
