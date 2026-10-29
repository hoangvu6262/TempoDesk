#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."

# This project owns its disposable volume; never use the developer's project name.
export COMPOSE_PROJECT_NAME="tempodesk_smoke_${RANDOM}_$$"
export POSTGRES_DB=tempodesk_smoke POSTGRES_PORT=0 API_PORT=0 WEB_PORT=0 MAILPIT_PORT=0
export POSTGRES_PASSWORD=smoke_admin_only
export MIGRATION_PASSWORD=smoke_migration_only RUNTIME_PASSWORD=smoke_runtime_only
export ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development
scratch=$(mktemp -d)
cleanup() {
    result=$?
    if [[ $result -ne 0 ]]; then
        mkdir -p artifacts
        docker compose logs --no-color > artifacts/stack.log 2>&1 || true
        echo 'Stack failed; see artifacts/stack.log.' >&2
    fi
    if ! docker compose down --volumes; then result=1; fi
    rm -r -- "$scratch"
    exit "$result"
}
trap cleanup EXIT

docker compose up --build -d --wait --wait-timeout 180
api_url="http://$(docker compose port api 8080)"
web_url="http://$(docker compose port web 8080)"

assert_health() {
    node -e '
      const assert = require("node:assert/strict");
      const value = JSON.parse(require("node:fs").readFileSync(0, "utf8"));
      assert.equal(value.status, process.argv[1]);
      assert.equal(typeof value.elapsedMs, "number");
      assert.ok(Number.isFinite(value.elapsedMs) && value.elapsedMs >= 0);
    ' "$1"
}
wait_for_http() {
    for attempt in {1..40}; do
        code=$(curl -sS --max-time 5 -o "$scratch/health.json" -w '%{http_code}' \
            "$api_url/health/ready" 2>/dev/null || true)
        if [[ "$code" == "$1" ]]; then return 0; fi
        sleep 1
    done
    echo "API readiness did not become HTTP $1." >&2
    return 1
}
wait_for_worker() {
    for attempt in {1..40}; do
        if docker compose exec -T worker dotnet TempoDesk.Worker.dll --health-check; then
            code=0
        else
            code=$?
        fi
        if [[ "$code" == "$1" ]]; then return 0; fi
        sleep 1
    done
    echo "Worker health did not become exit code $1." >&2
    return 1
}

curl -fsS "$api_url/health/live" | assert_health Healthy
curl -fsS "$api_url/health/ready" | assert_health Healthy
curl -fsS "$web_url/api/health/ready" | assert_health Healthy
curl -fsS "$web_url/" > "$scratch/index.html"
grep -q 'id="root"' "$scratch/index.html"
curl -fsS "$web_url/unknown-client-route" > "$scratch/fallback.html"
grep -q 'id="root"' "$scratch/fallback.html"
echo 'PASS: live/ready, browser API proxy and SPA fallback'

for status in 400 401 403 404 409 500; do
    code=$(curl -sS --max-time 10 -D "$scratch/headers" -o "$scratch/problem.json" \
        -w '%{http_code}' \
        -H 'traceparent: 00-11111111111111111111111111111111-2222222222222222-01' \
        -H 'Authorization: Bearer smoke-secret-authorization' \
        -H 'Cookie: session=smoke-secret-cookie' \
        "$api_url/dev/errors/$status?password=smoke-secret-query")
    [[ "$code" == "$status" ]]
    grep -qi 'content-type: application/problem+json' "$scratch/headers"
    node -e '
      const assert = require("node:assert/strict");
      const value = JSON.parse(require("node:fs").readFileSync(process.argv[1], "utf8"));
      assert.equal(value.status, Number(process.argv[2]));
      assert.equal(value.traceId, "11111111111111111111111111111111");
      for (const key of ["type", "title", "detail"]) assert.equal(typeof value[key], "string");
      if (value.status === 400) assert.ok(Object.keys(value.errors).length > 0);
    ' "$scratch/problem.json" "$status"
done
docker compose logs --no-color api worker > "$scratch/logs"
if grep -Eq 'smoke-secret-|smoke_runtime_only' "$scratch/logs"; then
    echo 'Sensitive request/configuration data appeared in application logs.' >&2
    exit 1
fi
echo 'PASS: Problem Details, W3C trace correlation and log redaction'

docker compose exec -T postgres psql -U postgres -d "$POSTGRES_DB" \
    -v ON_ERROR_STOP=1 -c "CREATE TABLE public.smoke_persistence (value text NOT NULL); INSERT INTO public.smoke_persistence VALUES ('kept');"
docker compose stop postgres
wait_for_http 503
assert_health Unhealthy < "$scratch/health.json"
curl -fsS "$api_url/health/live" | assert_health Healthy
wait_for_worker 1
docker compose start postgres
wait_for_http 200
assert_health Healthy < "$scratch/health.json"
wait_for_worker 0
echo 'PASS: API and Worker detect database failure and recover'

docker compose stop
docker compose up -d --wait --wait-timeout 180 --no-build
web_url="http://$(docker compose port web 8080)"
curl -fsS "$web_url/api/health/ready" | assert_health Healthy
value=$(docker compose exec -T postgres psql -U postgres -d "$POSTGRES_DB" \
    -v ON_ERROR_STOP=1 -Atc 'SELECT value FROM public.smoke_persistence')
[[ "$value" == kept ]]
echo 'PASS: full-stack restart preserves database data'
