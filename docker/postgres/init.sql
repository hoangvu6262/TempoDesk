\set ON_ERROR_STOP on
\getenv db POSTGRES_DB
\getenv migration_password MIGRATION_PASSWORD
\getenv runtime_password RUNTIME_PASSWORD

BEGIN;

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

COMMIT;
