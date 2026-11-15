# Development Swagger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Serve JWT-enabled Swagger UI and OpenAPI JSON in Development only.

**Architecture:** Swashbuckle generates one `v1` document from controller and minimal-endpoint ApiExplorer metadata and serves its embedded UI. Registration and middleware are colocated in `Program.cs`; environment gating prevents both routes from existing outside Development.

**Tech Stack:** .NET 10, ASP.NET Core, Swashbuckle.AspNetCore 10.3.1, OpenAPI 3.x, JWT Bearer.

## Global Constraints

- Swagger UI is available at `/swagger` only in Development.
- OpenAPI JSON is available at `/swagger/v1/swagger.json` only in Development.
- The document title is `TempoDesk API` and version is `v1`.
- Swagger UI supports JWT Bearer through its `Authorize` button and does not persist authorization.
- Production returns `404` for both Swagger routes.
- Do not change API behavior, authentication rules, response envelopes, or endpoint routes.

---

### Task 1: Add Development-only Swagger with JWT Bearer

**Files:**

- Modify: `server/src/TempoDesk.Api/TempoDesk.Api.csproj`
- Modify: `server/src/TempoDesk.Api/Program.cs`

**Interfaces:**

- Consumes: existing controller/minimal endpoint metadata and JWT Bearer authentication.
- Produces: `/swagger`, `/swagger/v1/swagger.json`, and the Swagger UI Bearer authorization control in Development.

- [ ] **Step 1: Capture the failing behavior before implementation**

With the current Development API running, execute:

```bash
test "$(curl -sS -o /dev/null -w '%{http_code}' \
  http://localhost:5023/swagger/index.html)" = "404"
```

Expected: exit code `0`, proving Swagger is absent before the change.

- [ ] **Step 2: Replace unused OpenAPI packages with Swashbuckle**

In `server/src/TempoDesk.Api/TempoDesk.Api.csproj`, replace:

```xml
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.10" />
<PackageReference Include="Microsoft.OpenApi" Version="2.12.2" />
```

with:

```xml
<PackageReference Include="Swashbuckle.AspNetCore" Version="10.3.1" />
```

- [ ] **Step 3: Register Swagger generation and JWT Bearer metadata**

Add this using to `Program.cs`:

```csharp
using Microsoft.OpenApi;
```

After `builder.Services.AddControllers();`, add:

```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TempoDesk API",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter a JWT access token."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});
```

- [ ] **Step 4: Map Swagger only in Development**

Replace the existing single-line Development error endpoint block:

```csharp
if (app.Environment.IsDevelopment())
    app.MapGet("/dev/errors/{status:int}", (int status) =>
        ErrorExamples.Throw(status));
```

with:

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "TempoDesk API v1");
    });
    app.MapGet("/dev/errors/{status:int}", (int status) =>
        ErrorExamples.Throw(status));
}
```

- [ ] **Step 5: Build the complete server solution**

Run:

```bash
dotnet build server/TempoDesk.sln --configuration Release
```

Expected: restore succeeds and build finishes with `0 Warning(s)` and
`0 Error(s)`.

- [ ] **Step 6: Verify Swagger in Development**

Rebuild the local API container:

```bash
docker compose up -d --build api
```

Then run:

```bash
curl --fail-with-body -sSL \
  http://localhost:5023/swagger/index.html | \
  rg -q '<title>Swagger UI</title>'

curl --fail-with-body -sS \
  http://localhost:5023/swagger/v1/swagger.json | \
  jq -e '
    .info.title == "TempoDesk API" and
    .info.version == "v1" and
    (.paths["/api/workspaces"] != null) and
    (.paths["/health/live"] != null) and
    (.components.securitySchemes.Bearer.type == "http") and
    (.components.securitySchemes.Bearer.scheme == "bearer")
  '
```

Expected: both pipelines exit `0`.

- [ ] **Step 7: Verify Swagger is absent in Production**

Start a temporary Production container using the Compose service
configuration:

```bash
swagger_production_container=$(docker compose run -d --rm \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -p 127.0.0.1:5024:8080 \
  api)
```

Wait until the health endpoint responds, then verify both Swagger routes:

```bash
for attempt in $(seq 1 20); do
  if curl -fsS http://localhost:5024/health/live >/dev/null; then
    break
  fi
  sleep 1
done

test "$(curl -sS -o /dev/null -w '%{http_code}' \
  http://localhost:5024/swagger/index.html)" = "404"

test "$(curl -sS -o /dev/null -w '%{http_code}' \
  http://localhost:5024/swagger/v1/swagger.json)" = "404"

docker stop "$swagger_production_container"
```

Expected: the health endpoint responds, both assertions exit `0`, and the
temporary container is removed because it was started with `--rm`.

- [ ] **Step 8: Check formatting and commit**

```bash
git diff --check
git status --short
git add \
  server/src/TempoDesk.Api/TempoDesk.Api.csproj \
  server/src/TempoDesk.Api/Program.cs
git commit -m "feat(api): add development Swagger"
```
