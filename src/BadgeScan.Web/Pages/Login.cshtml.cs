using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>Signing in with a username and password. A temporary password goes straight on to choosing a new one.</summary>
[AllowAnonymous]
public class LoginModel(AccountService accounts) : PageModel
{
    [BindProperty] public string? Username { get; set; }
    [BindProperty] public string? Password { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }

    /// <summary>A kiosk whose meeting ended is sent here on its way back to /kiosk.</summary>
    public bool MeetingEnded => ReturnUrl?.StartsWith("/kiosk", StringComparison.OrdinalIgnoreCase) == true;

    public IActionResult OnGet()
        => User.Identity?.IsAuthenticated == true ? Redirect(User.IsKiosk() ? "/kiosk" : "/") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var outcome = await accounts.SignInAsync(Username, Password, ct);
        if (outcome.Account is null)
        {
            Error = outcome.Error;
            return Page();
        }
        await SignIns.SignInAsync(HttpContext, SignIns.ForAccount(outcome.Account));
        if (outcome.Account.MustChangePassword) return Redirect("/account/password");
        // Only back to somewhere on this site, and never into a kiosk: that needs a new meeting.
        return Url.IsLocalUrl(ReturnUrl) && !MeetingEnded ? LocalRedirect(ReturnUrl!) : Redirect("/");
    }
}
