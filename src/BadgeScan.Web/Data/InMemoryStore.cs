using BadgeScan.Web.Models;

namespace BadgeScan.Web.Data;

/// <summary>
/// Badges and scans in two lists. Used by the tests and when there is no PGHOST, so the
/// site still runs on a laptop without PostgreSQL. Everything is gone when the site stops.
/// </summary>
public sealed class InMemoryStore : IBadgeScanStore
{
    private readonly object _lock = new();
    private readonly List<Badge> _badges = [];
    private readonly List<ScanResult> _scans = [];
    private long _nextBadgeId = 1;
    private long _nextScanId = 1;

    public Task<IReadOnlyList<Badge>> ListBadgesAsync(int? team, CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<Badge> list = _badges
                .Where(b => team is null || b.Team == team)
                .OrderBy(b => b.Team).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task<Badge?> GetBadgeAsync(long id, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(_badges.FirstOrDefault(b => b.Id == id));
    }

    public Task<IReadOnlyList<Badge>> FindBadgeOwnersAsync(string badgeNumber, int? team, CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<Badge> list = _badges.Where(b => b.BadgeNumber == badgeNumber && (team is null || b.Team == team)).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<(Badge Badge, bool Created)> UpsertBadgeAsync(int team, string name, string? badgeNumber, CancellationToken ct)
    {
        lock (_lock)
        {
            var existing = _badges.FirstOrDefault(b => b.Team == team && b.Name == name);
            var number = badgeNumber ?? existing?.BadgeNumber;
            if (number is not null)
            {
                var owner = _badges.FirstOrDefault(b => b.Team == team && b.BadgeNumber == number && b.Id != existing?.Id);
                if (owner is not null) throw new BadgeTakenException(number, owner.Name);
            }
            if (existing is null)
            {
                var created = new Badge(_nextBadgeId++, team, name, number);
                _badges.Add(created);
                return Task.FromResult((created, true));
            }
            var updated = existing with { BadgeNumber = number };
            _badges[_badges.IndexOf(existing)] = updated;
            return Task.FromResult((updated, false));
        }
    }

    public Task<Badge?> SetBadgeNumberAsync(long id, string? badgeNumber, CancellationToken ct)
    {
        lock (_lock)
        {
            var existing = _badges.FirstOrDefault(b => b.Id == id);
            if (existing is null) return Task.FromResult<Badge?>(null);
            if (badgeNumber is not null)
            {
                var owner = _badges.FirstOrDefault(b => b.Team == existing.Team && b.BadgeNumber == badgeNumber && b.Id != id);
                if (owner is not null) throw new BadgeTakenException(badgeNumber, owner.Name);
            }
            var updated = existing with { BadgeNumber = badgeNumber };
            _badges[_badges.IndexOf(existing)] = updated;
            return Task.FromResult<Badge?>(updated);
        }
    }

    public Task<bool> DeleteBadgeAsync(long id, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(_badges.RemoveAll(b => b.Id == id) > 0);
    }

    public Task<ScanResult?> InsertScanAsync(NewScan scan, CancellationToken ct)
    {
        lock (_lock)
        {
            if (scan.Source == "import" && _scans.Any(s => s.Source == "import" && s.Team == scan.Team && s.Name == scan.Name && s.ScannedCentral == scan.Central.Wall))
                return Task.FromResult<ScanResult?>(null);

            var firstOfDay = scan.Recognised
                && !_scans.Any(s => s.FirstOfDay && s.Team == scan.Team && s.Name == scan.Name && s.CentralDate == scan.Central.Date);

            var saved = new ScanResult
            {
                Id = _nextScanId++,
                BadgeNumber = scan.BadgeNumber,
                Team = scan.Team,
                Name = scan.Name,
                Recognised = scan.Recognised,
                FirstOfDay = firstOfDay,
                Stamp = scan.Central.Stamp,
                ScannedUtc = scan.Utc,
                ScannedCentral = scan.Central.Wall,
                CentralZone = scan.Central.ZoneAbbreviation,
                CentralDate = scan.Central.Date,
                Source = scan.Source,
                MeetingId = scan.MeetingId,
                OpenedBy = scan.OpenedBy,
            };
            _scans.Add(saved);
            return Task.FromResult<ScanResult?>(saved);
        }
    }

    public Task<string?> FirstStampAsync(int team, string name, DateOnly centralDate, CancellationToken ct)
    {
        lock (_lock)
            return Task.FromResult(_scans.FirstOrDefault(s => s.FirstOfDay && s.Team == team && s.Name == name && s.CentralDate == centralDate)?.Stamp);
    }

    public Task<IReadOnlyList<ScanResult>> ScansForDatesAsync(DateOnly from, DateOnly to, int? team, int? limit, CancellationToken ct)
    {
        lock (_lock)
        {
            var query = _scans
                .Where(s => s.CentralDate >= from && s.CentralDate <= to && (team is null || s.Team == team))
                .OrderByDescending(s => s.Id).AsEnumerable();
            if (limit is not null) query = query.Take(limit.Value);
            IReadOnlyList<ScanResult> list = query.ToList();
            return Task.FromResult(list);
        }
    }

    public Task<IReadOnlyList<ScanResult>> ScansSinceAsync(long afterId, int limit, CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<ScanResult> list = _scans.Where(s => s.Id > afterId).OrderBy(s => s.Id).Take(limit).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<IReadOnlyList<ScanResult>> ScansAfterAsync(long afterId, DateOnly centralDate, CancellationToken ct)
    {
        lock (_lock)
        {
            IReadOnlyList<ScanResult> list = _scans.Where(s => s.Id > afterId && s.CentralDate == centralDate).OrderBy(s => s.Id).ToList();
            return Task.FromResult(list);
        }
    }
}
