using BadgeScan.Web.Models;

namespace BadgeScan.Web.Data;

/// <summary>
/// Accounts, meetings and data keys in lists. Used by the tests and when there is no PGHOST.
/// Everything is gone when the site stops.
/// </summary>
public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly object _lock = new();
    private readonly List<Account> _accounts = [];
    private readonly List<Meeting> _meetings = [];
    private readonly List<(ApiKey Key, string Hash)> _keys = [];
    private long _nextId = 1;

    public Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<Account> list = _accounts.OrderBy(a => a.Disabled).ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<Account?> GetAccountAsync(long id, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(_accounts.FirstOrDefault(a => a.Id == id));
    }

    public Task<Account?> FindAccountAsync(string username, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(_accounts.FirstOrDefault(a => a.Username == username));
    }

    public Task<Account> AddAccountAsync(NewAccount account, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_accounts.Any(a => a.Username == account.Username)) throw new UsernameTakenException(account.Username);
            var created = new Account
            {
                Id = _nextId++,
                Username = account.Username,
                DisplayName = account.DisplayName,
                Title = account.Title,
                PasswordHash = account.PasswordHash,
                MustChangePassword = true,
                PasswordExpiresAt = account.PasswordExpiresAt,
                SecurityStamp = account.SecurityStamp,
                Disabled = false,
                FailedSignIns = 0,
                LockedUntil = null,
                LastSignInAt = null,
            };
            _accounts.Add(created);
            return Task.FromResult(created);
        }
    }

    public Task SetPasswordAsync(long id, string passwordHash, bool mustChange, DateTimeOffset? expiresAt, string securityStamp, CancellationToken ct)
        => Change(id, a => a with
        {
            PasswordHash = passwordHash, MustChangePassword = mustChange, PasswordExpiresAt = expiresAt,
            SecurityStamp = securityStamp, FailedSignIns = 0, LockedUntil = null,
        });

    public Task SetDisabledAsync(long id, bool disabled, string securityStamp, CancellationToken ct)
        => Change(id, a => a with { Disabled = disabled, SecurityStamp = securityStamp });

    public Task RecordSignInAsync(long id, DateTimeOffset at, CancellationToken ct)
        => Change(id, a => a with { LastSignInAt = at, FailedSignIns = 0, LockedUntil = null });

    public Task RecordFailedSignInAsync(long id, int limit, DateTimeOffset lockUntil, CancellationToken ct)
        => Change(id, a => a.FailedSignIns + 1 >= limit
            ? a with { FailedSignIns = 0, LockedUntil = lockUntil }
            : a with { FailedSignIns = a.FailedSignIns + 1 });

    public Task<Meeting> StartMeetingAsync(long openedBy, string openedByName, DateTimeOffset start, DateTimeOffset ends, DateOnly centralDate, CancellationToken ct)
    {
        lock (_lock)
        {
            var meeting = new Meeting
            {
                Id = _nextId++, OpenedBy = openedBy, OpenedByName = openedByName,
                StartedUtc = start, EndsUtc = ends, ClosedUtc = null, CentralDate = centralDate,
            };
            _meetings.Add(meeting);
            return Task.FromResult(meeting);
        }
    }

    public Task<Meeting?> GetMeetingAsync(long id, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(_meetings.FirstOrDefault(m => m.Id == id));
    }

    public Task<IReadOnlyList<Meeting>> OpenMeetingsAsync(DateTimeOffset now, CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<Meeting> list = _meetings.Where(m => m.IsOpenAt(now)).OrderBy(m => m.Id).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<bool> CloseMeetingAsync(long id, long closedBy, DateTimeOffset at, CancellationToken ct)
    {
        lock (_lock)
        {
            var i = _meetings.FindIndex(m => m.Id == id && m.ClosedUtc is null);
            if (i < 0) return Task.FromResult(false);
            _meetings[i] = _meetings[i] with { ClosedUtc = at };
            return Task.FromResult(true);
        }
    }

    public Task<ApiKey> AddApiKeyAsync(string label, string keyHash, string keyPrefix, long createdBy, DateTimeOffset at, CancellationToken ct)
    {
        lock (_lock)
        {
            var creator = _accounts.First(a => a.Id == createdBy);
            var key = new ApiKey
            {
                Id = _nextId++, Label = label, KeyPrefix = keyPrefix, CreatedByName = creator.DisplayName,
                CreatedAt = at, LastUsedAt = null, RevokedAt = null,
            };
            _keys.Add((key, keyHash));
            return Task.FromResult(key);
        }
    }

    public Task<IReadOnlyList<ApiKey>> ListApiKeysAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<ApiKey> list = _keys.Select(k => k.Key).OrderBy(k => k.RevokedAt is not null).ThenByDescending(k => k.Id).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<ApiKey?> FindApiKeyAsync(string keyHash, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult<ApiKey?>(_keys.FirstOrDefault(k => k.Hash == keyHash && k.Key.RevokedAt is null).Key);
    }

    public Task TouchApiKeyAsync(long id, DateTimeOffset at, CancellationToken ct)
    {
        lock (_lock)
        {
            var i = _keys.FindIndex(k => k.Key.Id == id);
            if (i >= 0) _keys[i] = (_keys[i].Key with { LastUsedAt = at }, _keys[i].Hash);
            return Task.CompletedTask;
        }
    }

    public Task<bool> RevokeApiKeyAsync(long id, DateTimeOffset at, CancellationToken ct)
    {
        lock (_lock)
        {
            var i = _keys.FindIndex(k => k.Key.Id == id && k.Key.RevokedAt is null);
            if (i < 0) return Task.FromResult(false);
            _keys[i] = (_keys[i].Key with { RevokedAt = at }, _keys[i].Hash);
            return Task.FromResult(true);
        }
    }

    private Task Change(long id, Func<Account, Account> change)
    {
        lock (_lock)
        {
            var i = _accounts.FindIndex(a => a.Id == id);
            if (i >= 0) _accounts[i] = change(_accounts[i]);
            return Task.CompletedTask;
        }
    }
}
