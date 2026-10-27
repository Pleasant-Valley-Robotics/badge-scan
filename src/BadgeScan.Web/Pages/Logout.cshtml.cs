using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>The Sign out button in the menu. A kiosk ends its meeting at /kiosk/end instead.</summary>
public class LogoutModel : PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await SignIns.SignOutAsync(HttpContext);
        return Redirect("/login");
    }
}
