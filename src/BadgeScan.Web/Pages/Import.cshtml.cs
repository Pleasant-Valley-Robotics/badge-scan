using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BadgeScan.Web.Pages;

/// <summary>Two forms, one page: people with their badges, and old scans. Both take a CSV file or pasted text.</summary>
public class ImportModel(Importer importer) : PageModel
{
    public ImportReport? Report { get; private set; }
    public string Kind { get; private set; } = "";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string kind, IFormFile? file, string? text, CancellationToken ct)
    {
        string csv;
        if (file is { Length: > 0 })
        {
            using var reader = new StreamReader(file.OpenReadStream());
            csv = await reader.ReadToEndAsync(ct);
        }
        else csv = text ?? "";

        Kind = kind == "scans" ? "Scans" : "Badges";
        Report = kind == "scans"
            ? await importer.ImportScansAsync(csv, ct)
            : await importer.ImportBadgesAsync(csv, ct);
        return Page();
    }
}
