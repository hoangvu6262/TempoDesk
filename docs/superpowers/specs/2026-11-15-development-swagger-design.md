# Development Swagger Design

## Goal

Expose interactive Swagger documentation for the TempoDesk API during local
and Development execution without publishing API discovery endpoints in
Production.

## Package Choice

Replace the currently unused direct references to `Microsoft.AspNetCore.OpenApi`
and `Microsoft.OpenApi` with `Swashbuckle.AspNetCore` version `10.3.1`.
Swashbuckle supplies document generation and the embedded Swagger UI while its
transitive dependencies provide the required OpenAPI model types.

Version `10.3.1` targets .NET 10 and is preferred over the newly released 11.x
major line to reduce upgrade risk for this small development feature.

## Runtime Configuration

Register Swagger generation with one `v1` document:

- title: `TempoDesk API`;
- version: `v1`;
- JWT Bearer security definition using the HTTP `bearer` scheme and JWT format;
- a global security requirement so protected operations can use the token
  entered through Swagger UI's `Authorize` button.

Only when `app.Environment.IsDevelopment()`:

- serve the OpenAPI document at `/swagger/v1/swagger.json`;
- serve Swagger UI at `/swagger`;
- keep the existing Development error-example endpoint.

No Swagger middleware or document endpoint is mapped outside Development.

## Security

Swagger does not bypass authentication or authorization. It only sends the
Bearer token supplied by the developer. The UI stores no application-managed
credential, and the implementation does not enable token persistence.

## Verification

- Build the complete server solution in Release configuration.
- Run the API with `ASPNETCORE_ENVIRONMENT=Development` and verify `/swagger`
  returns HTML and `/swagger/v1/swagger.json` returns an OpenAPI document that
  includes `/api/workspaces` and a Bearer security scheme.
- Run the API with `ASPNETCORE_ENVIRONMENT=Production` and verify both Swagger
  routes return `404`.

## Out of Scope

- XML documentation comments for every endpoint.
- API versioning.
- Publishing a static OpenAPI artifact.
- Exposing Swagger in Staging or Production.
- Changing endpoint behavior or response envelopes.
