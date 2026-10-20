using System.Text.RegularExpressions;

namespace BadgeScan.Web.Services;

/// <summary>
/// One badge, however it arrives. Card scanners pad to a fixed width, so the same badge
/// comes in as 0000000027 from the scanner and 27 when typed; both have to mean the same
/// person. Leading zeros go; a badge of all zeros is "0".
/// </summary>
public static partial class BadgeNumber
{
    public static string? Normalize(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return null;
        return LeadingZeros().Replace(s, "");
    }

    /// <summary>Only letters and digits: what a scanner can type and the database accepts.</summary>
    public static bool IsValid(string normalized) => Shape().IsMatch(normalized);

    [GeneratedRegex("^0+(?=.)")]
    private static partial Regex LeadingZeros();

    [GeneratedRegex("^[0-9A-Za-z]+$")]
    private static partial Regex Shape();
}
