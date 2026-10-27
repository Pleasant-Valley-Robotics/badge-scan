using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>
/// Giving someone a badge at the kiosk, without ending the meeting. Anyone at the kiosk can
/// do it; no sign-in is asked for. The browser stays the kiosk, and the person is then
/// checked in with that badge.
/// </summary>
[Authorize(Policy = SignIns.Policies.Scan)]
public class KioskAssignModel(IBadgeScanStore store, ScanRecorder recorder) : PageModel
{
    /// <summary>The "Team or role" choice for another team, shown with a number box.</summary>
    public const string OtherTeam = "other";

    [BindProperty(SupportsGet = true)] public string? Badge { get; set; }
    /// <summary>Someone already on the list, or null for a new person.</summary>
    [BindProperty] public long? PersonId { get; set; }
    [BindProperty] public string? NewName { get; set; }
    /// <summary>A team number, mentor, coach or advisor, or OtherTeam (then NewTeam is the number).</summary>
    [BindProperty] public string? NewGroup { get; set; }
    [BindProperty] public int? NewTeam { get; set; }

    public IReadOnlyList<Badge> People { get; private set; } = [];
    /// <summary>The teams people are already on, for the "Team or role" list.</summary>
    public IReadOnlyList<int> Teams { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!User.IsKiosk()) return Redirect("/badges");
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (HttpContext.CurrentMeeting() is not { } meeting) return Redirect("/");
        await LoadAsync(ct);

        var badge = BadgeNumber.Normalize(Badge);
        if (badge is null || !BadgeNumber.IsValid(badge)) return Fail("Scan the badge, or type its number (letters and digits only).");
        var newName = (NewName ?? "").Trim();
        if (PersonId is null && newName.Length == 0) return Fail("Pick who the badge is for, or type a new person's name.");
        var group = 0;
        if (PersonId is null && !TryGroup(out group))
            return Fail("Pick a team, or Mentor, Coach or Advisor, for the new person.");

        Badge? saved;
        try
        {
            saved = PersonId is { } id
                ? await store.SetBadgeNumberAsync(id, badge, ct)
                : (await store.UpsertBadgeAsync(group, newName, badge, ct)).Badge;
        }
        catch (BadgeTakenException ex)
        {
            return Fail(ex.Message);
        }
        if (saved is null) return Fail("That person is no longer on the list. Pick again.");

        // Now that the badge is theirs, this counts as their scan.
        var scan = await recorder.RecordAsync(badge, saved.Team, meeting.Id, meeting.OpenedByName, ct);
        var checkedIn = scan is ScanOutcome.Saved { Result.FirstOfDay: true } ? " and checked in" : "";
        TempData["Saved"] = $"Badge {badge} is now {saved.Name}'s ({saved.Group}){checkedIn}.";
        return Redirect("/kiosk");
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        People = await store.ListBadgesAsync(null, ct);
        Teams = People.Select(p => p.Team).Where(t => t > 0).Append(AppInfo.DefaultTeam).Distinct().Order().ToList();
    }

    private bool TryGroup(out int group)
    {
        if (NewGroup != OtherTeam) return Groups.TryParse(NewGroup, out group);
        group = NewTeam ?? 0;
        return group > 0;
    }

    private IActionResult Fail(string error)
    {
        Error = error;
        return Page();
    }
}
