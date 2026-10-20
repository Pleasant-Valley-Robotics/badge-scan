using System.Reflection;
using Npgsql;

namespace BadgeScan.Web.Data;

/// <summary>
/// Brings the database up to date when the site starts. Every file in Data/Migrations/
/// is plain SQL, applied once, in name order; the table schema_migrations remembers
/// which ones have run. To change the database, add a new file: never edit an old one.
/// </summary>
public static class Migrator
{
    private const string Prefix = "Migrations.";

    /// <summary>
    /// PostgreSQL may still be waking up when the container starts, so this tries a few
    /// times. If it still fails, the exception stops the site: /healthz never answers and
    /// the robotics server keeps the previous release running.
    /// </summary>
    public static async Task RunWithRetryAsync(NpgsqlDataSource db, ILogger log, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RunAsync(db, log, ct);
                return;
            }
            catch (Exception ex) when (attempt < 10 && ex is NpgsqlException or TimeoutException)
            {
                log.LogWarning("Database not ready (attempt {Attempt}): {Message}. Trying again in 3 seconds.", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    public static async Task RunAsync(NpgsqlDataSource db, ILogger log, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await Exec(conn, "CREATE TABLE IF NOT EXISTS schema_migrations (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())", ct);

        // Two containers starting at the same time take turns instead of both migrating.
        await Exec(conn, "SELECT pg_advisory_lock(7196)", ct);
        try
        {
            var applied = new HashSet<string>();
            await using (var cmd = new NpgsqlCommand("SELECT name FROM schema_migrations", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct)) applied.Add(reader.GetString(0));
            }

            var assembly = typeof(Migrator).Assembly;
            var resources = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);

            foreach (var resource in resources)
            {
                var name = resource[Prefix.Length..];
                if (applied.Contains(name)) continue;

                using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
                var sql = await reader.ReadToEndAsync(ct);

                await using var tx = await conn.BeginTransactionAsync(ct);
                await Exec(conn, sql, ct, tx);
                await using (var record = new NpgsqlCommand("INSERT INTO schema_migrations (name) VALUES ($1)", conn, tx))
                {
                    record.Parameters.Add(new NpgsqlParameter { Value = name });
                    await record.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                log.LogInformation("Applied migration {Name}", name);
            }
        }
        finally
        {
            await Exec(conn, "SELECT pg_advisory_unlock(7196)", CancellationToken.None);
        }
    }

    private static async Task Exec(NpgsqlConnection conn, string sql, CancellationToken ct, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
