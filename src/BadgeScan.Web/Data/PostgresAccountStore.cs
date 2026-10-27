using BadgeScan.Web.Models;
using Npgsql;
using NpgsqlTypes;

namespace BadgeScan.Web.Data;

/// <summary>Accounts, meetings and data keys in the team's PostgreSQL database on the robotics server.</summary>
public sealed class PostgresAccountStore(NpgsqlDataSource db) : IAccountStore
{
    private const string AccountColumns =
        "id, username, display_name, title, password_hash, must_change_password, password_expires_at, security_stamp, disabled, failed_sign_ins, locked_until, last_sign_in_at";
    private const string MeetingColumns = "id, opened_by, opened_by_name, started_utc, ends_utc, closed_utc, central_date";
    // k is api_keys (or, when adding one, the row just inserted); a is the account that made it.
    private const string KeyColumns = "k.id, k.label, k.key_prefix, a.display_name, k.created_at, k.last_used_at, k.revoked_at";
    private const string KeyFrom = "api_keys k JOIN accounts a ON a.id = k.created_by";

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"SELECT {AccountColumns} FROM accounts ORDER BY disabled, lower(display_name)");
        return await ReadAccounts(cmd, ct);
    }

    public async Task<Account?> GetAccountAsync(long id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"SELECT {AccountColumns} FROM accounts WHERE id = $1");
        cmd.Parameters.Add(Long(id));
        return (await ReadAccounts(cmd, ct)).FirstOrDefault();
    }

    public async Task<Account?> FindAccountAsync(string username, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"SELECT {AccountColumns} FROM accounts WHERE username = $1");
        cmd.Parameters.Add(Text(username));
        return (await ReadAccounts(cmd, ct)).FirstOrDefault();
    }

    public async Task<Account> AddAccountAsync(NewAccount account, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"""
            INSERT INTO accounts (username, display_name, title, password_hash, must_change_password, password_expires_at, security_stamp)
            VALUES ($1, $2, $3, $4, true, $5, $6)
            RETURNING {AccountColumns}
            """);
        cmd.Parameters.Add(Text(account.Username));
        cmd.Parameters.Add(Text(account.DisplayName));
        cmd.Parameters.Add(Text(account.Title));
        cmd.Parameters.Add(Text(account.PasswordHash));
        cmd.Parameters.Add(Time(account.PasswordExpiresAt));
        cmd.Parameters.Add(Text(account.SecurityStamp));
        try
        {
            return (await ReadAccounts(cmd, ct))[0];
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new UsernameTakenException(account.Username);
        }
    }

    public async Task SetPasswordAsync(long id, string passwordHash, bool mustChange, DateTimeOffset? expiresAt, string securityStamp, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("""
            UPDATE accounts SET password_hash = $2, must_change_password = $3, password_expires_at = $4, security_stamp = $5,
                                failed_sign_ins = 0, locked_until = NULL, updated_at = now()
            WHERE id = $1
            """);
        cmd.Parameters.Add(Long(id));
        cmd.Parameters.Add(Text(passwordHash));
        cmd.Parameters.Add(new NpgsqlParameter<bool> { TypedValue = mustChange });
        cmd.Parameters.Add(Time(expiresAt));
        cmd.Parameters.Add(Text(securityStamp));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetDisabledAsync(long id, bool disabled, string securityStamp, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("UPDATE accounts SET disabled = $2, security_stamp = $3, updated_at = now() WHERE id = $1");
        cmd.Parameters.Add(Long(id));
        cmd.Parameters.Add(new NpgsqlParameter<bool> { TypedValue = disabled });
        cmd.Parameters.Add(Text(securityStamp));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordSignInAsync(long id, DateTimeOffset at, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("UPDATE accounts SET last_sign_in_at = $2, failed_sign_ins = 0, locked_until = NULL WHERE id = $1");
        cmd.Parameters.Add(Long(id));
        cmd.Parameters.Add(Time(at));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordFailedSignInAsync(long id, int limit, DateTimeOffset lockUntil, CancellationToken ct)
    {
        // One statement, so two wrong tries at the same moment both count.
        await using var cmd = db.CreateCommand("""
            UPDATE accounts SET
              locked_until    = CASE WHEN failed_sign_ins + 1 >= $2 THEN $3 ELSE locked_until END,
              failed_sign_ins = CASE WHEN failed_sign_ins + 1 >= $2 THEN 0 ELSE failed_sign_ins + 1 END
            WHERE id = $1
            """);
        cmd.Parameters.Add(Long(id));
        cmd.Parameters.Add(new NpgsqlParameter<int> { TypedValue = limit });
        cmd.Parameters.Add(Time(lockUntil));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Meeting> StartMeetingAsync(long openedBy, string openedByName, DateTimeOffset start, DateTimeOffset ends, DateOnly centralDate, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"""
            INSERT INTO meetings (opened_by, opened_by_name, started_utc, ends_utc, central_date) VALUES ($1, $2, $3, $4, $5)
            RETURNING {MeetingColumns}
            """);
        cmd.Parameters.Add(Long(openedBy));
        cmd.Parameters.Add(Text(openedByName));
        cmd.Parameters.Add(Time(start));
        cmd.Parameters.Add(Time(ends));
        cmd.Parameters.Add(new NpgsqlParameter<DateOnly> { TypedValue = centralDate, NpgsqlDbType = NpgsqlDbType.Date });
        return (await ReadMeetings(cmd, ct))[0];
    }

    public async Task<Meeting?> GetMeetingAsync(long id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"SELECT {MeetingColumns} FROM meetings WHERE id = $1");
        cmd.Parameters.Add(Long(id));
        return (await ReadMeetings(cmd, ct)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<Meeting>> OpenMeetingsAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"SELECT {MeetingColumns} FROM meetings WHERE closed_utc IS NULL AND ends_utc > $1 ORDER BY id");
        cmd.Parameters.Add(Time(now));
        return await ReadMeetings(cmd, ct);
    }

    public async Task<bool> CloseMeetingAsync(long id, long closedBy, DateTimeOffset at, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("UPDATE meetings SET closed_utc = $3, closed_by = $2 WHERE id = $1 AND closed_utc IS NULL");
        cmd.Parameters.Add(Long(id));
        cmd.Parameters.Add(Long(closedBy));
        cmd.Parameters.Add(Time(at));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<ApiKey> AddApiKeyAsync(string label, string keyHash, string keyPrefix, long createdBy, DateTimeOffset at, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"""
            WITH k AS (
              INSERT INTO api_keys (label, key_hash, key_prefix, created_by, created_at) VALUES ($1, $2, $3, $4, $5)
              RETURNING *)
            SELECT {KeyColumns} FROM k JOIN accounts a ON a.id = k.created_by
            """);
        cmd.Parameters.Add(Text(label));
        cmd.Parameters.Add(Text(keyHash));
        cmd.Parameters.Add(Text(keyPrefix));
        cmd.Parameters.Add(Long(createdBy));
        cmd.Parameters.Add(Time(at));
        return (await ReadKeys(cmd, ct))[0];
    }

    public async Task<IReadOnlyList<ApiKey>> ListApiKeysAsync(CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"SELECT {KeyColumns} FROM {KeyFrom} ORDER BY k.revoked_at IS NOT NULL, k.id DESC");
        return await ReadKeys(cmd, ct);
    }

    public async Task<ApiKey?> FindApiKeyAsync(string keyHash, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand($"SELECT {KeyColumns} FROM {KeyFrom} WHERE k.key_hash = $1 AND k.revoked_at IS NULL");
        cmd.Parameters.Add(Text(keyHash));
        return (await ReadKeys(cmd, ct)).FirstOrDefault();
    }

    public async Task TouchApiKeyAsync(long id, DateTimeOffset at, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("UPDATE api_keys SET last_used_at = $2 WHERE id = $1");
        cmd.Parameters.Add(Long(id));
        cmd.Parameters.Add(Time(at));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> RevokeApiKeyAsync(long id, DateTimeOffset at, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("UPDATE api_keys SET revoked_at = $2 WHERE id = $1 AND revoked_at IS NULL");
        cmd.Parameters.Add(Long(id));
        cmd.Parameters.Add(Time(at));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // ---- helpers ----

    private static NpgsqlParameter Text(string? value) => new NpgsqlParameter<string?> { TypedValue = value, NpgsqlDbType = NpgsqlDbType.Text };
    private static NpgsqlParameter Long(long value) => new NpgsqlParameter<long> { TypedValue = value };
    private static NpgsqlParameter Time(DateTimeOffset? value)
        => new NpgsqlParameter<DateTime?> { TypedValue = value?.UtcDateTime, NpgsqlDbType = NpgsqlDbType.TimestampTz };

    private static DateTimeOffset? ReadTime(NpgsqlDataReader r, int i)
        => r.IsDBNull(i) ? null : new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc));

    private static async Task<IReadOnlyList<Account>> ReadAccounts(NpgsqlCommand cmd, CancellationToken ct)
    {
        var list = new List<Account>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new Account
            {
                Id = r.GetInt64(0),
                Username = r.GetString(1),
                DisplayName = r.GetString(2),
                Title = r.GetString(3),
                PasswordHash = r.GetString(4),
                MustChangePassword = r.GetBoolean(5),
                PasswordExpiresAt = ReadTime(r, 6),
                SecurityStamp = r.GetString(7),
                Disabled = r.GetBoolean(8),
                FailedSignIns = r.GetInt32(9),
                LockedUntil = ReadTime(r, 10),
                LastSignInAt = ReadTime(r, 11),
            });
        }
        return list;
    }

    private static async Task<IReadOnlyList<Meeting>> ReadMeetings(NpgsqlCommand cmd, CancellationToken ct)
    {
        var list = new List<Meeting>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new Meeting
            {
                Id = r.GetInt64(0),
                OpenedBy = r.GetInt64(1),
                OpenedByName = r.GetString(2),
                StartedUtc = ReadTime(r, 3)!.Value,
                EndsUtc = ReadTime(r, 4)!.Value,
                ClosedUtc = ReadTime(r, 5),
                CentralDate = r.GetFieldValue<DateOnly>(6),
            });
        }
        return list;
    }

    private static async Task<IReadOnlyList<ApiKey>> ReadKeys(NpgsqlCommand cmd, CancellationToken ct)
    {
        var list = new List<ApiKey>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ApiKey
            {
                Id = r.GetInt64(0),
                Label = r.GetString(1),
                KeyPrefix = r.GetString(2),
                CreatedByName = r.GetString(3),
                CreatedAt = ReadTime(r, 4)!.Value,
                LastUsedAt = ReadTime(r, 5),
                RevokedAt = ReadTime(r, 6),
            });
        }
        return list;
    }
}
