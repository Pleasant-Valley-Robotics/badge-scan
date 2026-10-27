using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>
/// Who can sign in, and the data keys other programs read scans with. A new password or key
/// is shown once, on the page that made it, and never again: it isn't stored anywhere.
/// </summary>
public class AccountsModel(AccountService service, IAccountStore store, TimeProvider clock) : PageModel
{
    public IReadOnlyList<Account> Accounts { get; private set; } = [];
    public IReadOnlyList<ApiKey> Keys { get; private set; } = [];
    public long MyId => HttpContext.CurrentAccount()!.Id;
    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>Something to show once: a temporary password or a new key.</summary>
    public sealed record OneTime(string Heading, string Secret, string Note);
    public OneTime? Shown { get; private set; }
    public string? Error { get; private set; }
    public string? Message { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await Load(ct);

    public async Task<IActionResult> OnPostAddAsync(string? username, string? displayName, string? title, CancellationToken ct)
    {
        try
        {
            var (account, temporary) = await service.CreateAsync(username, displayName, title, ct);
            Shown = new($"Give {account.DisplayName} this temporary password", temporary,
                $"They sign in as {account.Username} and then choose their own. It works for {AccountService.TemporaryPasswordLife.Days} days. It won't be shown again.");
        }
        catch (Exception ex) when (ex is ArgumentException or UsernameTakenException)
        {
            Error = ex.Message;
        }
        await Load(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostResetAsync(long id, CancellationToken ct)
    {
        if (await store.GetAccountAsync(id, ct) is { } account)
        {
            var temporary = await service.ResetPasswordAsync(id, ct);
            Shown = new($"New temporary password for {account.DisplayName}", temporary,
                $"Their old password no longer works, and they're signed out everywhere. It works for {AccountService.TemporaryPasswordLife.Days} days. It won't be shown again.");
        }
        await Load(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync(long id, bool off, CancellationToken ct)
    {
        if (id == MyId) Error = "You can't turn off your own account.";
        else if (await store.GetAccountAsync(id, ct) is { } account)
        {
            await service.SetDisabledAsync(id, off, ct);
            Message = off ? $"{account.DisplayName} can no longer sign in, and is signed out everywhere." : $"{account.DisplayName} can sign in again.";
        }
        await Load(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateKeyAsync(string? label, CancellationToken ct)
    {
        try
        {
            var (key, secret) = await service.CreateApiKeyAsync(label, MyId, ct);
            Shown = new($"Data key for {key.Label}", secret,
                "Put it in that program's settings (never in its code or a browser page). It can read scans and nothing else. It won't be shown again.");
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
        }
        await Load(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeKeyAsync(long id, CancellationToken ct)
    {
        if (await store.RevokeApiKeyAsync(id, clock.GetUtcNow(), ct)) Message = "Key revoked. Anything using it can no longer read scans.";
        await Load(ct);
        return Page();
    }

    public string Status(Account a)
    {
        if (a.Disabled) return "turned off";
        if (a.LockedUntil > Now) return $"locked until {IndexModel.Clock(a.LockedUntil.Value)} (wrong passwords)";
        if (a.MustChangePassword)
            return a.PasswordExpiresAt <= Now ? "temporary password expired: reset it" : "hasn't signed in yet (temporary password)";
        return "";
    }

    public static string When(DateTimeOffset? utc)
        => utc is null ? "never" : CentralTime.From(utc.Value).Wall.ToString("MMM d, h:mm tt");

    private async Task Load(CancellationToken ct)
    {
        Accounts = await store.ListAccountsAsync(ct);
        Keys = await store.ListApiKeysAsync(ct);
    }
}
