using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>Loads the people for the Badges page. Changes go through /api/badges from badges.js.</summary>
public class BadgesModel(IBadgeScanStore store) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? Team { get; set; }

    public IReadOnlyList<Badge> People { get; private set; } = [];
    public IReadOnlyList<int> Teams { get; private set; } = [];
    public int Unassigned => People.Count(p => p.BadgeNumber is null);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var all = await store.ListBadgesAsync(null, ct);
        Teams = all.Select(p => p.Team).Append(AppInfo.DefaultTeam).Distinct().Order().ToList();
        People = Team is null ? all : all.Where(p => p.Team == Team).ToList();
    }
}
