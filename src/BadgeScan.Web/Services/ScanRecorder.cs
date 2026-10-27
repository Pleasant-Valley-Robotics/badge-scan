using BadgeScan.Web.Data;
using BadgeScan.Web.Models;

namespace BadgeScan.Web.Services;

/// <summary>What one of these means to the kiosk, and the HTTP status it becomes.</summary>
public abstract record ScanOutcome
{
    public sealed record Empty : ScanOutcome;
    public sealed record NotRecognised(string Badge) : ScanOutcome;
    public sealed record Ambiguous(string Badge) : ScanOutcome;
    public sealed record Saved(ScanResult Result) : ScanOutcome;
}

/// <summary>
/// What happens when a badge is scanned: find whose it is, save the scan with the time in
/// UTC and Central, and tell every open kiosk. The first scan of the day checks a person
/// in; later ones are saved too, and the kiosk says they already scanned.
/// </summary>
public sealed class ScanRecorder(IBadgeScanStore store, ScanBroadcaster broadcaster, TimeProvider clock)
{
    /// <summary>A badge scanned at a kiosk. meeting is the kiosk's; openedBy is the name of whoever opened it (or who is scanning, without a meeting).</summary>
    public async Task<ScanOutcome> RecordAsync(string? rawBadge, int? team, long? meeting, string? openedBy, CancellationToken ct)
    {
        var badge = BadgeNumber.Normalize(rawBadge);
        if (badge is null) return new ScanOutcome.Empty();

        var now = clock.GetUtcNow();
        var central = CentralTime.From(now);

        var owners = BadgeNumber.IsValid(badge)
            ? await store.FindBadgeOwnersAsync(badge, team, ct)
            : [];

        if (owners.Count != 1)
        {
            // Saved anyway: a lead can see which unknown badges were waved at the door.
            var unknown = await store.InsertScanAsync(new NewScan(badge, null, null, Recognised: false, now, central, "kiosk", meeting, openedBy), ct);
            if (unknown is not null) broadcaster.Publish(unknown);
            return owners.Count == 0 ? new ScanOutcome.NotRecognised(badge) : new ScanOutcome.Ambiguous(badge);
        }

        var owner = owners[0];
        var saved = (await store.InsertScanAsync(new NewScan(badge, owner.Team, owner.Name, Recognised: true, now, central, "kiosk", meeting, openedBy), ct))!;
        if (saved.Already)
        {
            saved = saved with { FirstStamp = await store.FirstStampAsync(owner.Team, owner.Name, central.Date, ct) };
        }
        broadcaster.Publish(saved);
        return new ScanOutcome.Saved(saved);
    }
}
