using Npgsql;

namespace Cloud;

public sealed class Pg(NpgsqlDataSource source)
{
    public Task<NpgsqlConnection> Open() => source.OpenConnectionAsync().AsTask();

    public async Task<List<Dictionary<string, object?>>> Query(string sql, params (string Name, object? Value)[] values)
    {
        await using var command = source.CreateCommand(sql);
        Add(command, values);
        return await Read(command);
    }

    public static void Add(NpgsqlCommand command, params (string Name, object? Value)[] values)
    {
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public static async Task<List<Dictionary<string, object?>>> Read(NpgsqlCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var parts = reader.GetName(i).Split('_');
                var name = parts[0] + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }
        return rows;
    }

    public async Task Migrate()
    {
        await using var connection = await Open();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(7341206); CREATE TABLE IF NOT EXISTS schema_migrations(version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());", connection, transaction))
            await guard.ExecuteNonQueryAsync();
        await using var check = new NpgsqlCommand("SELECT count(*) FROM schema_migrations WHERE version=1", connection, transaction);
        if ((long)(await check.ExecuteScalarAsync())! == 0)
        {
            using var input = typeof(Pg).Assembly.GetManifestResourceStream("Cloud.Migrations.001_initial.sql")!;
            using var reader = new StreamReader(input);
            await using var migration = new NpgsqlCommand(await reader.ReadToEndAsync(), connection, transaction);
            await migration.ExecuteNonQueryAsync();
            await using var record = new NpgsqlCommand("INSERT INTO schema_migrations(version) VALUES(1)", connection, transaction);
            await record.ExecuteNonQueryAsync();
        }
        await using (var cleanup = new NpgsqlCommand("DELETE FROM sessions WHERE expires_at < now();", connection, transaction))
            await cleanup.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
