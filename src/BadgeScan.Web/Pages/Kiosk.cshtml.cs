using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>The kiosk, for a browser running a meeting. Anyone else is sent to start one.</summary>
[Authorize(Policy = SignIns.Policies.Scan)]
public class KioskModel : PageModel
{
    public Meeting Meeting { get; private set; } = null!;

    public IActionResult OnGet()
    {
        if (HttpContext.CurrentMeeting() is not { } meeting) return Redirect("/");
        Meeting = meeting;
        return Page();
    }
}
