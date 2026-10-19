-- Apply as tempodesk_migrator; the caller manages the transaction.
CREATE TABLE public.schema_history (
    file_name text PRIMARY KEY CHECK (file_name <> ''),
    checksum text NOT NULL CHECK (checksum ~ '^[0-9a-f]{64}$'),
    applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    duration_ms bigint NOT NULL CHECK (duration_ms >= 0)
);

-- Application processes must not change migration history.
REVOKE ALL ON TABLE public.schema_history FROM tempodesk_runtime;
