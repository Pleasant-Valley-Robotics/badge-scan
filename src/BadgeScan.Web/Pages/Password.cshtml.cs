using BadgeScan.Web.Data;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>Choosing your own password: required after a temporary one, allowed any time.</summary>
public class PasswordModel(AccountService accounts, IAccountStore store) : PageModel
{
    [BindProperty] public string? Current { get; set; }
    [BindProperty] public string? Chosen { get; set; }
    [BindProperty] public string? Repeated { get; set; }
    public string? Error { get; private set; }
    public bool Required => HttpContext.CurrentAccount()?.MustChangePassword == true;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var account = HttpContext.CurrentAccount()!;
        Error = await accounts.ChangePasswordAsync(account, Current, Chosen, Repeated, ct);
        if (Error is not null) return Page();

        // A new password ends every sign-in made with the old one, this one included: sign in again.
        var updated = await store.GetAccountAsync(account.Id, ct);
        await SignIns.SignInAsync(HttpContext, SignIns.ForAccount(updated!));
        TempData["Saved"] = "Your new password is saved.";
        return Redirect("/");
    }
}
