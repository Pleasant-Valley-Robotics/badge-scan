using System.Globalization;
using System.Text.Json.Serialization;
using BadgeScan.Web.Services;

namespace BadgeScan.Web.Models;

/// <summary>A scan about to be saved. The store adds the id and decides first_of_day.</summary>
public sealed record NewScan(
    string? BadgeNumber,
    int? Team,
    string? Name,
    bool Recognised,
    DateTimeOffset Utc,
    CentralTime.Parts Central,
    string Source,
    long? MeetingId = null,
    string? OpenedBy = null);

/// <summary>
/// One saved scan, as the kiosk, the lists and the live stream all see it. The JSON names
/// are camelCase: "badge", "firstOfDay", "scannedUtc", and so on.
/// </summary>
public sealed record ScanResult
{
    public bool Ok => true;
    public required long Id { get; init; }
    [JsonPropertyName("badge")] public required string? BadgeNumber { get; init; }
    /// <summary>The person's team, or a role's code (Groups). Null when the badge wasn't recognised.</summary>
    [JsonIgnore] public required int? Team { get; init; }
    [JsonPropertyName("team")] public int? TeamNumber => Groups.TeamNumber(Team);
    /// <summary>"mentor", "coach" or "advisor" for someone not on a team.</summary>
    public string? Role => Groups.RoleName(Team);
    /// <summary>"Team 7196" or "Mentor", for the kiosk screen.</summary>
    public string Group => Groups.Label(Team);
    public required string? Name { get; init; }
    public required bool Recognised { get; init; }
    public required bool FirstOfDay { get; init; }
    /// <summary>True when this person had already scanned in earlier that day.</summary>
    public bool Already => Recognised && !FirstOfDay;
    /// <summary>When Already: the stamp of the scan that did check them in. Filled by the kiosk's save, not by lists.</summary>
    public string? FirstStamp { get; init; }
    public required string Stamp { get; init; }
    public required DateTimeOffset ScannedUtc { get; init; }
    public required DateTime ScannedCentral { get; init; }
    public required string CentralZone { get; init; }
    public required DateOnly CentralDate { get; init; }
    public required string Source { get; init; }
    /// <summary>The meeting the scan came through, and the name of whoever opened it. Null for older and imported scans.</summary>
    public long? MeetingId { get; init; }
    public string? OpenedBy { get; init; }

    /// <summary>For the kiosk screen: "Tuesday, October 27, 2026".</summary>
    public string DateText => ScannedCentral.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
    /// <summary>For the kiosk screen: "5:17 PM CDT".</summary>
    public string TimeText => $"{ScannedCentral.ToString("h:mm tt", CultureInfo.InvariantCulture)} {CentralZone}";
}
