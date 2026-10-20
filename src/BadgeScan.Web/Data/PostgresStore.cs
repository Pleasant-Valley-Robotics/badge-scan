using BadgeScan.Web.Models;
using Npgsql;
using NpgsqlTypes;

namespace BadgeScan.Web.Data;

/// <summary>Badges and scans in the team's PostgreSQL database on the robotics server.</summary>
public sealed class PostgresStore(NpgsqlDataSource db) : IBadgeScanStore
{
    private const string BadgeColumns = "id, team_number, person_name, badge_number";
    private const string ScanColumns =
        "id, badge_number, team_number, person_name, recognised, first_of_day, scanned_utc, scanned_central, central_zone, central_date, stamp, source";

    public async Task<IReadOnlyList<Badge>> ListBadgesAsync(int? team, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            $"SELECT {BadgeColumns} FROM badges WHERE $1::int IS NULL OR team_number = $1 ORDER BY team_number, lower(person_name)");
        cmd.Parameters.Add(Int(team));
        return await ReadBadges(cmd, ct);
    }

    public async Task<Badge?> GetBadgeAsync(long id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"SELECT {BadgeColumns} FROM badges WHERE id = $1");
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id });
        return (await ReadBadges(cmd, ct)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<Badge>> FindBadgeOwnersAsync(string badgeNumber, int? team, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            $"SELECT {BadgeColumns} FROM badges WHERE badge_number = $1 AND ($2::int IS NULL OR team_number = $2)");
        cmd.Parameters.Add(Text(badgeNumber));
        cmd.Parameters.Add(Int(team));
        return await ReadBadges(cmd, ct);
    }

    public async Task<(Badge Badge, bool Created)> UpsertBadgeAsync(int team, string name, string? badgeNumber, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"""
            INSERT INTO badges (team_number, person_name, badge_number) VALUES ($1, $2, $3)
            ON CONFLICT (team_number, person_name)
              DO UPDATE SET badge_number = COALESCE(EXCLUDED.badge_number, badges.badge_number), updated_at = now()
            RETURNING {BadgeColumns}, (xmax = 0) AS created
            """);
        cmd.Parameters.Add(Int(team));
        cmd.Parameters.Add(Text(name));
        cmd.Parameters.Add(Text(badgeNumber));
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return (ReadBadge(reader), reader.GetBoolean(4));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw await Taken(team, badgeNumber!, ct);
        }
    }

    public async Task<Badge?> SetBadgeNumberAsync(long id, string? badgeNumber, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            $"UPDATE badges SET badge_number = $2, updated_at = now() WHERE id = $1 RETURNING {BadgeColumns}");
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id });
        cmd.Parameters.Add(Text(badgeNumber));
        try
        {
            return (await ReadBadges(cmd, ct)).FirstOrDefault();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var person = await GetBadgeAsync(id, ct);
            throw await Taken(person!.Team, badgeNumber!, ct);
        }
    }

    public async Task<bool> DeleteBadgeAsync(long id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("DELETE FROM badges WHERE id = $1");
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = id });
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<ScanResult?> InsertScanAsync(NewScan scan, CancellationToken ct)
    {
        // first_of_day is decided inside the INSERT, so two scans a moment apart can't both
        // be first. If they still race, the unique index refuses the second; it is saved
        // again as a repeat.
        for (var attempt = 0; ; attempt++)
        {
            var forceRepeat = attempt > 0;
            var conflict = scan.Source == "import"
                ? "ON CONFLICT (team_number, person_name, scanned_central) WHERE source = 'import' DO NOTHING"
                : "";
            await using var cmd = db.CreateCommand($"""
                INSERT INTO badge_scans (badge_number, team_number, person_name, recognised, first_of_day,
                                         scanned_utc, scanned_central, central_zone, central_date, stamp, source)
                VALUES ($1, $2, $3, $4,
                        $4 AND NOT $11 AND NOT EXISTS (
                            SELECT 1 FROM badge_scans s
                            WHERE s.team_number = $2 AND s.person_name = $3 AND s.central_date = $9 AND s.first_of_day),
                        $5, $6, $7, $9, $8, $10)
                {conflict}
                RETURNING {ScanColumns}
                """);
            cmd.Parameters.Add(Text(scan.BadgeNumber));
            cmd.Parameters.Add(Int(scan.Team));
            cmd.Parameters.Add(Text(scan.Name));
            cmd.Parameters.Add(new NpgsqlParameter<bool> { TypedValue = scan.Recognised });
            cmd.Parameters.Add(new NpgsqlParameter<DateTime> { TypedValue = scan.Utc.UtcDateTime, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            cmd.Parameters.Add(new NpgsqlParameter<DateTime> { TypedValue = DateTime.SpecifyKind(scan.Central.Wall, DateTimeKind.Unspecified), NpgsqlDbType = NpgsqlDbType.Timestamp });
            cmd.Parameters.Add(Text(scan.Central.ZoneAbbreviation));
            cmd.Parameters.Add(Text(scan.Central.Stamp));
            cmd.Parameters.Add(new NpgsqlParameter<DateOnly> { TypedValue = scan.Central.Date, NpgsqlDbType = NpgsqlDbType.Date });
            cmd.Parameters.Add(Text(scan.Source));
            cmd.Parameters.Add(new NpgsqlParameter<bool> { TypedValue = forceRepeat });
            try
            {
                var rows = await ReadScans(cmd, ct);
                return rows.FirstOrDefault();
            }
            catch (PostgresException ex) when (attempt == 0 && ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == "badge_scans_first_of_day_uq")
            {
                // Lost the race to be first. Save it as a repeat.
            }
        }
    }

    public async Task<string?> FirstStampAsync(int team, string name, DateOnly centralDate, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            "SELECT stamp FROM badge_scans WHERE team_number = $1 AND person_name = $2 AND central_date = $3 AND first_of_day LIMIT 1");
        cmd.Parameters.Add(Int(team));
        cmd.Parameters.Add(Text(name));
        cmd.Parameters.Add(new NpgsqlParameter<DateOnly> { TypedValue = centralDate, NpgsqlDbType = NpgsqlDbType.Date });
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task<IReadOnlyList<ScanResult>> ScansForDatesAsync(DateOnly from, DateOnly to, int? team, int? limit, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"""
            SELECT {ScanColumns} FROM badge_scans
            WHERE central_date BETWEEN $1 AND $2 AND ($3::int IS NULL OR team_number = $3)
            ORDER BY id DESC
            LIMIT $4
            """);
        cmd.Parameters.Add(new NpgsqlParameter<DateOnly> { TypedValue = from, NpgsqlDbType = NpgsqlDbType.Date });
        cmd.Parameters.Add(new NpgsqlParameter<DateOnly> { TypedValue = to, NpgsqlDbType = NpgsqlDbType.Date });
        cmd.Parameters.Add(Int(team));
        cmd.Parameters.Add(Int(limit));   // LIMIT NULL means no limit
        return await ReadScans(cmd, ct);
    }

    public async Task<IReadOnlyList<ScanResult>> ScansAfterAsync(long afterId, DateOnly centralDate, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            $"SELECT {ScanColumns} FROM badge_scans WHERE id > $1 AND central_date = $2 ORDER BY id");
        cmd.Parameters.Add(new NpgsqlParameter<long> { TypedValue = afterId });
        cmd.Parameters.Add(new NpgsqlParameter<DateOnly> { TypedValue = centralDate, NpgsqlDbType = NpgsqlDbType.Date });
        return await ReadScans(cmd, ct);
    }

    // ---- helpers ----

    private async Task<BadgeTakenException> Taken(int team, string badgeNumber, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("SELECT person_name FROM badges WHERE team_number = $1 AND badge_number = $2");
        cmd.Parameters.Add(Int(team));
        cmd.Parameters.Add(Text(badgeNumber));
        var owner = await cmd.ExecuteScalarAsync(ct) as string ?? "someone else";
        return new BadgeTakenException(badgeNumber, owner);
    }

    private static NpgsqlParameter Text(string? value) => new NpgsqlParameter<string?> { TypedValue = value, NpgsqlDbType = NpgsqlDbType.Text };
    private static NpgsqlParameter Int(int? value) => new NpgsqlParameter<int?> { TypedValue = value, NpgsqlDbType = NpgsqlDbType.Integer };

    private static Badge ReadBadge(NpgsqlDataReader r)
        => new(r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3));

    private static async Task<IReadOnlyList<Badge>> ReadBadges(NpgsqlCommand cmd, CancellationToken ct)
    {
        var list = new List<Badge>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(ReadBadge(reader));
        return list;
    }

    private static async Task<IReadOnlyList<ScanResult>> ReadScans(NpgsqlCommand cmd, CancellationToken ct)
    {
        var list = new List<ScanResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ScanResult
            {
                Id = reader.GetInt64(0),
                BadgeNumber = reader.IsDBNull(1) ? null : reader.GetString(1),
                Team = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Name = reader.IsDBNull(3) ? null : reader.GetString(3),
                Recognised = reader.GetBoolean(4),
                FirstOfDay = reader.GetBoolean(5),
                ScannedUtc = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)),
                ScannedCentral = DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Unspecified),
                CentralZone = reader.GetString(8),
                CentralDate = reader.GetFieldValue<DateOnly>(9),
                Stamp = reader.GetString(10),
                Source = reader.GetString(11),
            });
        }
        return list;
    }
}
