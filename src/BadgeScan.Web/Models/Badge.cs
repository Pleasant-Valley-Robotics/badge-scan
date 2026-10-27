using System.Text.Json.Serialization;

namespace BadgeScan.Web.Models;

/// <summary>
/// A person and the badge they hold, if any. Team is their team, or a role's code for a
/// mentor, coach or advisor (Groups); the JSON says "team": 7196 or null, and "role".
/// </summary>
public sealed record Badge(long Id, [property: JsonIgnore] int Team, string Name, string? BadgeNumber)
{
    [JsonPropertyName("team")] public int? TeamNumber => Groups.TeamNumber(Team);
    public string? Role => Groups.RoleName(Team);
    /// <summary>"Team 7196" or "Mentor".</summary>
    public string Group => Groups.Label(Team);
}

/// <summary>Thrown when a badge number is already another person's on that team.</summary>
public sealed class BadgeTakenException(string badgeNumber, string ownerName)
    : Exception($"Badge {badgeNumber} already belongs to {ownerName}.")
{
    public string BadgeNumber { get; } = badgeNumber;
    public string OwnerName { get; } = ownerName;
}
