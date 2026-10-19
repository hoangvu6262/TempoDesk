-- Run as tempodesk_migrator. Test data is rolled back.
BEGIN;

INSERT INTO public.schema_history (file_name, checksum, duration_ms)
VALUES ('__schema_history_test__.sql', repeat('a', 64), 0);

DO $$
BEGIN
    ASSERT (
        SELECT tableowner = 'tempodesk_migrator'
        FROM pg_tables
        WHERE schemaname = 'public' AND tablename = 'schema_history'
    ), 'Migration role must own schema_history';

    ASSERT NOT has_table_privilege(
        'tempodesk_runtime', 'public.schema_history', 'SELECT'
    ), 'Runtime must not read migration history';
    ASSERT NOT has_table_privilege(
        'tempodesk_runtime', 'public.schema_history', 'INSERT, UPDATE, DELETE'
    ), 'Runtime must not modify migration history';

    BEGIN
        INSERT INTO public.schema_history (file_name, checksum, duration_ms)
        VALUES ('__invalid_checksum__.sql', 'invalid', 0);
        RAISE EXCEPTION 'Invalid checksum was accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO public.schema_history (file_name, checksum, duration_ms)
        VALUES ('__invalid_duration__.sql', repeat('b', 64), -1);
        RAISE EXCEPTION 'Negative duration was accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;
END $$;

ROLLBACK;
