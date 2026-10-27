using BadgeScan.Web.Models;

namespace BadgeScan.Web.Data;

/// <summary>
/// Where badges and scans are kept. PostgresStore is the real one; InMemoryStore stands
/// in for the tests and for running without a database.
/// </summary>
public interface IBadgeScanStore
{
    Task<IReadOnlyList<Badge>> ListBadgesAsync(int? team, CancellationToken ct);
    Task<Badge?> GetBadgeAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<Badge>> FindBadgeOwnersAsync(string badgeNumber, int? team, CancellationToken ct);

    /// <summary>
    /// Adds the person, or finds them by team and name. A badge number given here is set;
    /// null leaves whatever badge they already have. Throws BadgeTakenException if the
    /// number is someone else's on that team. Returns the row and whether it was new.
    /// </summary>
    Task<(Badge Badge, bool Created)> UpsertBadgeAsync(int team, string name, string? badgeNumber, CancellationToken ct);

    /// <summary>Assign, change or (with null) clear a person's badge. Null result: no such person.</summary>
    Task<Badge?> SetBadgeNumberAsync(long id, string? badgeNumber, CancellationToken ct);
    Task<bool> DeleteBadgeAsync(long id, CancellationToken ct);

    /// <summary>
    /// Saves a scan and decides first_of_day: true for a recognised person's first scan of
    /// that Central day. Returns null only for an imported scan that was already there.
    /// </summary>
    Task<ScanResult?> InsertScanAsync(NewScan scan, CancellationToken ct);

    /// <summary>The stamp of the scan that checked this person in on that day, if any.</summary>
    Task<string?> FirstStampAsync(int team, string name, DateOnly centralDate, CancellationToken ct);

    /// <summary>Scans between two Central dates (inclusive), newest first.</summary>
    Task<IReadOnlyList<ScanResult>> ScansForDatesAsync(DateOnly from, DateOnly to, int? team, int? limit, CancellationToken ct);

    /// <summary>Every scan with an id above the given one, oldest first, at most limit: how another program catches up.</summary>
    Task<IReadOnlyList<ScanResult>> ScansSinceAsync(long afterId, int limit, CancellationToken ct);

    /// <summary>Scans on one Central date with an id above the given one, oldest first: what a reconnecting kiosk missed.</summary>
    Task<IReadOnlyList<ScanResult>> ScansAfterAsync(long afterId, DateOnly centralDate, CancellationToken ct);
}
