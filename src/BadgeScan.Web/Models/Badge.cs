namespace BadgeScan.Web.Models;

/// <summary>A person on a team and the badge they hold, if any.</summary>
public sealed record Badge(long Id, int Team, string Name, string? BadgeNumber);

/// <summary>Thrown when a badge number is already another person's on that team.</summary>
public sealed class BadgeTakenException(string badgeNumber, string ownerName)
    : Exception($"Badge {badgeNumber} already belongs to {ownerName}.")
{
    public string BadgeNumber { get; } = badgeNumber;
    public string OwnerName { get; } = ownerName;
}
