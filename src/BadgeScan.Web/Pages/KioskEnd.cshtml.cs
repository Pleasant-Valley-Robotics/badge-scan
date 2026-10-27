using BadgeScan.Web.Data;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>
/// Ending the meeting on the kiosk. Any lead, coach or mentor may, with their own password;
/// the browser is then signed in as them, back on the Start page.
/// </summary>
[Authorize(Policy = SignIns.Policies.Scan)]
public class KioskEndModel(AccountService accounts, IAccountStore store, IBadgeScanStore scans, TimeProvider clock) : PageModel
{
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? Password { get; set; }
    public string? Error { get; private set; }

    public IActionResult OnGet() => User.IsKiosk() ? Page() : Redirect("/");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (HttpContext.CurrentMeeting() is not { } meeting) return Redirect("/");
        var outcome = await accounts.SignInAsync(Username, Password, ct);
        if (outcome.Account is not { } account)
        {
            Error = outcome.Error;
            return Page();
        }

        await store.CloseMeetingAsync(meeting.Id, account.Id, clock.GetUtcNow(), ct);
        var during = (await scans.ScansForDatesAsync(meeting.CentralDate, CentralTime.Today(clock), null, null, ct))
            .Where(s => s.MeetingId == meeting.Id).ToList();
        TempData["Saved"] = $"Meeting ended: {during.Count(s => s.FirstOfDay)} checked in, {during.Count} scans in all.";

        await SignIns.SignInAsync(HttpContext, SignIns.ForAccount(account));
        return Redirect(account.MustChangePassword ? "/account/password" : "/");
    }
}
