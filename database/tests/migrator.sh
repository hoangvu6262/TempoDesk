#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."

if ! docker compose up -d --wait postgres; then
    docker compose logs --no-color postgres
    exit 1
fi
docker compose build migrator

test_dir=$(mktemp -d)
chmod 755 "$test_dir"
test_db="tempodesk_migration_check_${RANDOM}_$$"
database_created=false
cleanup() {
    if "$database_created"; then
        docker compose exec -T postgres dropdb -U postgres "$test_db"
    fi
    rm -r -- "$test_dir"
}
trap cleanup EXIT

docker compose exec -T postgres createdb -U postgres \
    --owner=tempodesk_migrator --template=template0 "$test_db"
database_created=true
docker compose exec -T postgres psql -U postgres -d "$test_db" \
    -v ON_ERROR_STOP=1 -c 'ALTER DEFAULT PRIVILEGES FOR ROLE tempodesk_migrator IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tempodesk_runtime;'
migrations=(database/migrations/*.sql)
cp "${migrations[@]}" "$test_dir/"
base_count=${#migrations[@]}

run_migrations() {
    docker compose run --rm --no-deps --pull never -T \
        -e TEMPODESK_DB_NAME="$test_db" \
        -v "$test_dir:/app/database/migrations:ro" migrator
}
query() {
    docker compose exec -T postgres psql -U postgres -d "$test_db" \
        -v ON_ERROR_STOP=1 -Atc "$1"
}

output=$(run_migrations)
[[ "$output" == *"APPLY 202610051730_create_schema_history.sql"* ]]
[[ $(query 'SELECT count(*) FROM public.schema_history') == "$base_count" ]]
docker compose exec -T postgres sh -c \
    'PGPASSWORD="$MIGRATION_PASSWORD" psql -h 127.0.0.1 -U tempodesk_migrator -d "$1" -v ON_ERROR_STOP=1' \
    sh "$test_db" < database/tests/schema_history.sql
echo 'PASS: empty database and schema-history constraints'

cat > "$test_dir/999901010001_create_probe.sql" <<'SQL'
CREATE TABLE public.migration_probe (id integer PRIMARY KEY);
SQL
output=$(run_migrations)
[[ "$output" == *"APPLY 999901010001_create_probe.sql"* ]]
output=$(run_migrations)
[[ "$output" == *"SKIP  999901010001_create_probe.sql"* ]]
[[ "$output" != *"APPLY "* ]]
[[ $(query 'SELECT count(*) FROM public.schema_history') == "$((base_count + 1))" ]]
echo 'PASS: apply and repeated run'

# Scaffold in a temporary source tree, never over the developer's generated code.
mkdir "$test_dir/workspace"
tar --exclude='*/bin' --exclude='*/obj' --exclude='*/Persistence/Generated' \
    --exclude='*/appsettings.Local.json' -cf - \
    global.json .config/dotnet-tools.json server/Directory.Build.props \
    server/src database/scaffold.sh | tar -xf - -C "$test_dir/workspace"
endpoint=$(docker compose port postgres 5432)
migration_password=$(docker compose exec -T postgres sh -c 'printf "%s" "$MIGRATION_PASSWORD"')
TEMPODESK_DB_HOST=127.0.0.1 TEMPODESK_DB_PORT="${endpoint##*:}" \
    TEMPODESK_DB_NAME="$test_db" TEMPODESK_MIGRATION_PASSWORD="$migration_password" \
    bash "$test_dir/workspace/database/scaffold.sh" public.migration_probe
generated="$test_dir/workspace/server/src/TempoDesk.Infrastructure/Persistence/Generated"
[[ -f "$generated/TempoDeskDbContext.cs" && -f "$generated/Entities/MigrationProbe.cs" ]]
! grep -Eq 'OnConfiguring|SchemaHistory|Password=' "$generated/TempoDeskDbContext.cs"
! grep -Fq -- "$migration_password" "$generated/TempoDeskDbContext.cs"
generated_files=("$generated"/*.cs "$generated"/Entities/*.cs)
[[ ${#generated_files[@]} -eq 2 ]]
dotnet build "$test_dir/workspace/server/src/TempoDesk.Infrastructure" --no-restore \
    --disable-build-servers -m:1
echo 'PASS: scaffold stays in Generated, contains no credentials, and compiles'

if output=$(TEMPODESK_MIGRATION_PASSWORD=unused bash database/scaffold.sh public.schema_history 2>&1); then
    echo 'FAIL: scaffold accepted migration history as a business table' >&2
    exit 1
fi
[[ "$output" == *'Not an allowed business table:'* ]]

cp "$test_dir/999901010001_create_probe.sql" "$test_dir/original.txt"
printf '\n-- Changed after applying.\n' >> "$test_dir/999901010001_create_probe.sql"
if output=$(run_migrations 2>&1); then
    echo 'FAIL: modified migration was accepted' >&2
    exit 1
fi
[[ "$output" == *'Migration đã chạy nhưng bị sửa:'* ]]
cp "$test_dir/original.txt" "$test_dir/999901010001_create_probe.sql"
echo 'PASS: checksum mismatch is rejected'

cat > "$test_dir/999901010002_test_rollback.sql" <<'SQL'
CREATE TABLE public.rollback_probe (id integer PRIMARY KEY);
SELECT 1 / 0;
SQL
if output=$(run_migrations 2>&1); then
    echo 'FAIL: invalid migration was accepted' >&2
    exit 1
fi
[[ "$output" == *'22012'* ]]
[[ $(query "SELECT to_regclass('public.rollback_probe') IS NULL") == t ]]
[[ $(query 'SELECT count(*) FROM public.schema_history') == "$((base_count + 1))" ]]
echo 'PASS: SQL failure rolls back schema and history'
