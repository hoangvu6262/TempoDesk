using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

try
{
    var folder = Path.GetFullPath("database/migrations");
    var files = Directory.GetFiles(folder, "*.sql")
        .OrderBy(Path.GetFileName, StringComparer.Ordinal)
        .ToArray();

    const string initial = "202610051730_create_schema_history.sql";

    if (files.Length == 0 || Path.GetFileName(files[0]) != initial)
        throw new InvalidOperationException(
            $"Migration đầu tiên phải là {initial}.");

    var password = Environment.GetEnvironmentVariable(
        "TEMPODESK_MIGRATION_PASSWORD");

    if (string.IsNullOrWhiteSpace(password))
        throw new InvalidOperationException(
            "Thiếu TEMPODESK_MIGRATION_PASSWORD.");

    var settings = new NpgsqlConnectionStringBuilder
    {
        Host = Environment.GetEnvironmentVariable("TEMPODESK_DB_HOST")
            ?? "localhost",
        Port = int.Parse(
            Environment.GetEnvironmentVariable("TEMPODESK_DB_PORT")
            ?? "5432"),
        Database = Environment.GetEnvironmentVariable("TEMPODESK_DB_NAME")
            ?? "tempodesk",
        Username = "tempodesk_migrator",
        Password = password,
        // Local PostgreSQL uses password authentication, not Kerberos.
        GssEncryptionMode = GssEncryptionMode.Disable
    };

    await using var source = NpgsqlDataSource.Create(
        settings.ConnectionString);
    await using var connection = await source.OpenConnectionAsync();

    foreach (var file in files)
    {
        var name = Path.GetFileName(file);
        var bytes = await File.ReadAllBytesAsync(file);
        var checksum = Convert.ToHexString(
            SHA256.HashData(bytes)).ToLowerInvariant();
        var sql = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

        await using var transaction =
            await connection.BeginTransactionAsync();

        // Serialize migration runners using the same database.
        await using (var gate = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(20261005)",
            connection, transaction))
        {
            await gate.ExecuteNonQueryAsync();
        }

        await using var exists = new NpgsqlCommand(
            "SELECT to_regclass('public.schema_history') IS NOT NULL",
            connection, transaction);

        var hasHistory = (bool)(await exists.ExecuteScalarAsync())!;

        if (hasHistory)
        {
            await using var lookup = new NpgsqlCommand(
                """
                SELECT checksum FROM public.schema_history
                WHERE file_name = @name
                """,
                connection, transaction);

            lookup.Parameters.AddWithValue("name", name);
            var stored = await lookup.ExecuteScalarAsync() as string;

            if (stored is not null)
            {
                if (stored != checksum)
                    throw new InvalidOperationException(
                        $"Migration đã chạy nhưng bị sửa: {name}");

                Console.WriteLine($"SKIP  {name}");
                await transaction.CommitAsync();
                continue;
            }

            if (name == initial)
                throw new InvalidOperationException(
                    "schema_history đã tồn tại nhưng migration đầu tiên "
                    + "chưa được đăng ký. Thực hiện bước ghi nhận thủ công.");
        }

        var timer = Stopwatch.StartNew();

        await using (var apply = new NpgsqlCommand(
            sql, connection, transaction))
        {
            await apply.ExecuteNonQueryAsync();
        }

        timer.Stop();

        await using var record = new NpgsqlCommand(
            """
            INSERT INTO public.schema_history
                (file_name, checksum, duration_ms)
            VALUES (@name, @checksum, @duration)
            """,
            connection, transaction);

        record.Parameters.AddWithValue("name", name);
        record.Parameters.AddWithValue("checksum", checksum);
        record.Parameters.AddWithValue(
            "duration", timer.ElapsedMilliseconds);

        await record.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        Console.WriteLine($"APPLY {name}");
    }

    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
