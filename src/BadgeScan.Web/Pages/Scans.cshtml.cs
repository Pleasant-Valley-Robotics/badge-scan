using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>The scans between two Central dates. Today, unless the form says otherwise.</summary>
public class ScansModel(IBadgeScanStore store, TimeProvider clock) : PageModel
{
    [BindProperty(SupportsGet = true)] public DateOnly? Date { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true)] public int? Team { get; set; }

    /// <summary>The dates actually shown, once the blanks are filled in.</summary>
    public DateOnly From => Date!.Value;
    public DateOnly Until => To!.Value;

    public IReadOnlyList<ScanResult> Scans { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Date ??= CentralTime.Today(clock);
        To ??= Date;
        if (To < Date) To = Date;
        Scans = await store.ScansForDatesAsync(Date.Value, To.Value, Team, 2000, ct);
    }
}
