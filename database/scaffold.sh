#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ $# -eq 0 ]]; then
    echo 'Usage: bash database/scaffold.sh public.table_name [public.other_table ...]' >&2
    exit 1
fi
: "${TEMPODESK_MIGRATION_PASSWORD:?Set TEMPODESK_MIGRATION_PASSWORD before scaffolding}"

tables=()
for table in "$@"; do
    if [[ ! "$table" =~ ^([a-z_][a-z0-9_]*\.)?[a-z_][a-z0-9_]*$ ||
          "$table" == schema_history || "$table" == public.schema_history ]]; then
        echo "Not an allowed business table: $table" >&2
        exit 1
    fi
    tables+=(--table "$table")
done

project=server/src/TempoDesk.Infrastructure
dotnet tool restore
dotnet restore "$project"
export PGPASSWORD="$TEMPODESK_MIGRATION_PASSWORD"
connection="Host=${TEMPODESK_DB_HOST:-localhost};Port=${TEMPODESK_DB_PORT:-5432};Database=${TEMPODESK_DB_NAME:-tempodesk};Username=tempodesk_migrator;GSS Encryption Mode=Disable"
dotnet ef dbcontext scaffold "$connection" Npgsql.EntityFrameworkCore.PostgreSQL \
    --project "$project" --startup-project "$project" \
    --context TempoDeskDbContext \
    --context-dir Persistence/Generated \
    --output-dir Persistence/Generated/Entities \
    --namespace TempoDesk.Infrastructure.Persistence.Generated.Entities \
    --context-namespace TempoDesk.Infrastructure.Persistence.Generated \
    --no-onconfiguring --force "${tables[@]}"
