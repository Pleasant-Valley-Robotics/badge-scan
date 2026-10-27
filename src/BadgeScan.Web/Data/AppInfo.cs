namespace BadgeScan.Web.Data;

/// <summary>The few facts about this site that more than one page needs.</summary>
public static class AppInfo
{
    public const string Name = "PV Attendance";

    /// <summary>The team a badge belongs to when nobody says otherwise: Radical, FTC 7196.</summary>
    public const int DefaultTeam = 7196;

    /// <summary>The menu at the top of every page for a signed-in lead, coach or mentor. The tests open every address in it.</summary>
    public static readonly (string Label, string Href)[] Nav =
    [
        ("Start", "/"),
        ("Badges", "/badges"),
        ("Scans", "/scans"),
        ("Import", "/import"),
        ("Accounts", "/accounts"),
    ];

    public const string PvSite = "https://www.firstpvrobotics.com";
}

/// <summary>Whether scans are being saved to PostgreSQL or only kept in memory.</summary>
public sealed record StoreInfo(bool InMemory);
