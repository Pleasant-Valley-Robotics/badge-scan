using System.Globalization;

namespace BadgeScan.Web.Models;

/// <summary>
/// Who a person scans in as: a team (7196, 6093, ...) or, for the adults, Mentor, Coach or
/// Advisor. In the database a person's "team_number" holds the team, or a negative code for
/// a role (migration 0003). Each role works like a team of its own: one check-in a day,
/// and one badge number per group. This class is the one place that knows the codes.
/// </summary>
public static class Groups
{
    public sealed record Role(int Code, string Name, string Label);

    public static readonly Role[] Roles =
    [
        new(-1, "mentor", "Mentor"),
        new(-2, "coach", "Coach"),
        new(-3, "advisor", "Advisor"),
    ];

    public static bool IsValid(int code) => code > 0 || Roles.Any(r => r.Code == code);

    /// <summary>The team number, or null for a role.</summary>
    public static int? TeamNumber(int? code) => code > 0 ? code : null;

    /// <summary>"mentor", "coach", "advisor", or null for a team.</summary>
    public static string? RoleName(int? code) => Roles.FirstOrDefault(r => r.Code == code)?.Name;

    /// <summary>"Team 7196" or "Mentor".</summary>
    public static string Label(int? code)
        => code is null ? "" : code > 0 ? $"Team {code}" : Roles.FirstOrDefault(r => r.Code == code)?.Label ?? "";

    /// <summary>"7196" or "Mentor": for table columns and spreadsheets.</summary>
    public static string Short(int? code)
        => code is null ? "" : code > 0 ? code.Value.ToString(CultureInfo.InvariantCulture) : Label(code);

    /// <summary>A team number, or mentor, coach or advisor in any case. False for anything else.</summary>
    public static bool TryParse(string? text, out int code)
    {
        code = 0;
        var t = (text ?? "").Trim();
        if (t.StartsWith("team ", StringComparison.OrdinalIgnoreCase)) t = t[5..].Trim();
        if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var team) && team > 0)
        {
            code = team;
            return true;
        }
        var role = Roles.FirstOrDefault(r => r.Name.Equals(t, StringComparison.OrdinalIgnoreCase));
        if (role is null) return false;
        code = role.Code;
        return true;
    }

    public const string Expected = "a team number, or mentor, coach or advisor";
}
