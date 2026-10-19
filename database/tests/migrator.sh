#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."

docker compose up -d --wait postgres
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
cp database/migrations/202610051730_create_schema_history.sql "$test_dir/"

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
[[ $(query 'SELECT count(*) FROM public.schema_history') == 1 ]]
docker compose exec -T postgres sh -c \
    'PGPASSWORD="$MIGRATION_PASSWORD" psql -h 127.0.0.1 -U tempodesk_migrator -d "$1" -v ON_ERROR_STOP=1' \
    sh "$test_db" < database/tests/schema_history.sql
echo 'PASS: empty database and schema-history constraints'

cat > "$test_dir/202610051800_create_probe.sql" <<'SQL'
CREATE TABLE public.migration_probe (id integer PRIMARY KEY);
SQL
output=$(run_migrations)
[[ "$output" == *"APPLY 202610051800_create_probe.sql"* ]]
output=$(run_migrations)
[[ "$output" == *"SKIP  202610051800_create_probe.sql"* ]]
[[ "$output" != *"APPLY "* ]]
[[ $(query 'SELECT count(*) FROM public.schema_history') == 2 ]]
echo 'PASS: apply and repeated run'

cp "$test_dir/202610051800_create_probe.sql" "$test_dir/original.txt"
printf '\n-- Changed after applying.\n' >> "$test_dir/202610051800_create_probe.sql"
if output=$(run_migrations 2>&1); then
    echo 'FAIL: modified migration was accepted' >&2
    exit 1
fi
[[ "$output" == *'Migration đã chạy nhưng bị sửa:'* ]]
cp "$test_dir/original.txt" "$test_dir/202610051800_create_probe.sql"
echo 'PASS: checksum mismatch is rejected'

cat > "$test_dir/202610051810_test_rollback.sql" <<'SQL'
CREATE TABLE public.rollback_probe (id integer PRIMARY KEY);
SELECT 1 / 0;
SQL
if output=$(run_migrations 2>&1); then
    echo 'FAIL: invalid migration was accepted' >&2
    exit 1
fi
[[ "$output" == *'22012'* ]]
[[ $(query "SELECT to_regclass('public.rollback_probe') IS NULL") == t ]]
[[ $(query 'SELECT count(*) FROM public.schema_history') == 2 ]]
echo 'PASS: SQL failure rolls back schema and history'
