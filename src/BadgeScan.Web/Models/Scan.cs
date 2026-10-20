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
    string Source);

/// <summary>
/// One saved scan, as the kiosk, the lists and the live stream all see it. The JSON names
/// are camelCase: "badge", "firstOfDay", "scannedUtc", and so on.
/// </summary>
public sealed record ScanResult
{
    public bool Ok => true;
    public required long Id { get; init; }
    [JsonPropertyName("badge")] public required string? BadgeNumber { get; init; }
    public required int? Team { get; init; }
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
}
