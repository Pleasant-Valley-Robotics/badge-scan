using System.Globalization;

namespace BadgeScan.Web.Services;

/// <summary>
/// The team's wall clock: US Central, which is CST in winter and CDT in summer, whatever
/// time zone the kiosk laptop or the server happens to be set to.
/// </summary>
public static class CentralTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static readonly string[] Days = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
    private const string StampFormat = "MM/dd/yyyy|HH:mm";

    /// <summary>The same instant as Central sees it: the wall clock, CST or CDT, the date, and the stamp.</summary>
    public sealed record Parts(DateTime Wall, string ZoneAbbreviation, DateOnly Date, string Stamp);

    public static Parts From(DateTimeOffset instant)
    {
        var wall = TimeZoneInfo.ConvertTimeFromUtc(instant.UtcDateTime, Zone);
        var abbreviation = Zone.IsDaylightSavingTime(wall) ? "CDT" : "CST";
        return new Parts(wall, abbreviation, DateOnly.FromDateTime(wall), Stamp(wall));
    }

    /// <summary>The stamp a scan leaves: Th10/08/2026|17:17. Two-letter day, date, 24-hour time.</summary>
    public static string Stamp(DateTime wall)
        => $"{Days[(int)wall.DayOfWeek]}{wall.ToString(StampFormat, CultureInfo.InvariantCulture)}";

    /// <summary>
    /// A stamp back to the instant it names, for importing old scans. The repeated hour when
    /// clocks fall back is taken as standard time; a stamp carries no way to tell.
    /// </summary>
    public static bool TryParseStamp(string? stamp, out DateTimeOffset instant)
    {
        instant = default;
        if (stamp is null || stamp.Length < 3) return false;
        if (!DateTime.TryParseExact(stamp[2..], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall)) return false;
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(wall)) return false;
        var utc = Zone.IsAmbiguousTime(wall)
            ? wall - Zone.BaseUtcOffset
            : TimeZoneInfo.ConvertTimeToUtc(wall, Zone);
        instant = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        return true;
    }

    /// <summary>Today's date on the Central clock.</summary>
    public static DateOnly Today(TimeProvider clock) => From(clock.GetUtcNow()).Date;
}
