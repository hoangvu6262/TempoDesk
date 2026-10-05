#!/usr/bin/env bash
set -euo pipefail

psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=ON_ERROR_STOP=1 \
  --set=db="$POSTGRES_DB" \
  --set=migration_password="$MIGRATION_PASSWORD" \
  --set=runtime_password="$RUNTIME_PASSWORD" <<'SQL'
CREATE ROLE tempodesk_migrator LOGIN PASSWORD :'migration_password';
CREATE ROLE tempodesk_runtime LOGIN PASSWORD :'runtime_password';

REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db"
  TO tempodesk_migrator, tempodesk_runtime;

REVOKE ALL ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO tempodesk_migrator;
GRANT USAGE ON SCHEMA public TO tempodesk_runtime;

ALTER DEFAULT PRIVILEGES FOR ROLE tempodesk_migrator
  IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES
  TO tempodesk_runtime;

ALTER DEFAULT PRIVILEGES FOR ROLE tempodesk_migrator
  IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES
  TO tempodesk_runtime;
SQL