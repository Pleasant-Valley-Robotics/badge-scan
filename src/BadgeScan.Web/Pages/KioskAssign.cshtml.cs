using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>
/// Giving someone a badge at the kiosk, without ending the meeting. A lead, coach or mentor
/// approves it with their username and password, which do just this: the browser stays the
/// kiosk. The person is then checked in with that badge.
/// </summary>
[Authorize(Policy = SignIns.Policies.Scan)]
public class KioskAssignModel(AccountService accounts, IBadgeScanStore store, ScanRecorder recorder) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Badge { get; set; }
    /// <summary>Someone already on the list, or null for a new person.</summary>
    [BindProperty] public long? PersonId { get; set; }
    [BindProperty] public string? NewName { get; set; }
    [BindProperty] public int? NewTeam { get; set; }
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? Password { get; set; }

    public IReadOnlyList<Badge> People { get; private set; } = [];
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!User.IsKiosk()) return Redirect("/badges");
        People = await store.ListBadgesAsync(null, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (HttpContext.CurrentMeeting() is not { } meeting) return Redirect("/");
        People = await store.ListBadgesAsync(null, ct);

        var badge = BadgeNumber.Normalize(Badge);
        if (badge is null || !BadgeNumber.IsValid(badge)) return Fail("Scan the badge, or type its number (letters and digits only).");
        var newName = (NewName ?? "").Trim();
        if (PersonId is null && newName.Length == 0) return Fail("Pick who the badge is for, or type a new person's name.");
        if (PersonId is null && NewTeam is not > 0) return Fail("A new person needs a team number.");

        // The password is checked last, so a mistake above doesn't count as a wrong password.
        var outcome = await accounts.SignInAsync(Username, Password, ct);
        if (outcome.Account is null) return Fail(outcome.Error!);

        Badge? saved;
        try
        {
            saved = PersonId is { } id
                ? await store.SetBadgeNumberAsync(id, badge, ct)
                : (await store.UpsertBadgeAsync(NewTeam!.Value, newName, badge, ct)).Badge;
        }
        catch (BadgeTakenException ex)
        {
            return Fail(ex.Message);
        }
        if (saved is null) return Fail("That person is no longer on the list. Pick again.");

        // Now that the badge is theirs, this counts as their scan.
        var scan = await recorder.RecordAsync(badge, saved.Team, meeting.Id, meeting.OpenedByName, ct);
        var checkedIn = scan is ScanOutcome.Saved { Result.FirstOfDay: true } ? " and checked in" : "";
        TempData["Saved"] = $"Badge {badge} is now {saved.Name}'s (team {saved.Team}){checkedIn}. Approved by {outcome.Account.DisplayName}.";
        return Redirect("/kiosk");
    }

    private IActionResult Fail(string error)
    {
        Error = error;
        Password = null;
        return Page();
    }
}
