using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>
/// Where a lead, coach or mentor lands after signing in: start a meeting (which turns this
/// browser into the kiosk), see the meetings open now, and today's numbers.
/// </summary>
public class IndexModel(IAccountStore accounts, IBadgeScanStore scans, TimeProvider clock) : PageModel
{
    /// <summary>How long a meeting may be chosen to last. It can always be ended sooner.</summary>
    public static readonly int[] Hours = [1, 2, 3, 4, 5, 6, 8, 12];
    public const int DefaultHours = 3;

    public IReadOnlyList<Meeting> OpenMeetings { get; private set; } = [];
    public int CheckedInToday { get; private set; }
    public int UnknownToday { get; private set; }
    public string? Message { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Message = TempData["Saved"] as string;
        OpenMeetings = await accounts.OpenMeetingsAsync(clock.GetUtcNow(), ct);
        var today = await scans.ScansForDatesAsync(CentralTime.Today(clock), CentralTime.Today(clock), null, null, ct);
        CheckedInToday = today.Count(s => s.FirstOfDay);
        UnknownToday = today.Count(s => !s.Recognised);
    }

    /// <summary>Start a meeting: this browser stops being signed in as you and becomes the kiosk until the meeting ends.</summary>
    public async Task<IActionResult> OnPostStartAsync(int hours, CancellationToken ct)
    {
        if (!Hours.Contains(hours)) hours = DefaultHours;
        var account = HttpContext.CurrentAccount()!;
        var now = clock.GetUtcNow();
        var meeting = await accounts.StartMeetingAsync(account.Id, account.DisplayName, now, now.AddHours(hours), CentralTime.From(now).Date, ct);
        await SignIns.SignInAsync(HttpContext, SignIns.ForMeeting(meeting));
        return Redirect("/kiosk");
    }

    /// <summary>End a meeting from here, for a kiosk left running somewhere else.</summary>
    public async Task<IActionResult> OnPostEndAsync(long id, CancellationToken ct)
    {
        if (await accounts.CloseMeetingAsync(id, HttpContext.CurrentAccount()!.Id, clock.GetUtcNow(), ct))
            TempData["Saved"] = "Meeting ended. That kiosk now shows the sign-in page.";
        return Redirect("/");
    }

    public static string Clock(DateTimeOffset utc) => CentralTime.From(utc).Wall.ToString("h:mm tt");
}
