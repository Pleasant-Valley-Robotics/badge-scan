using BadgeScan.Web.Data;
using BadgeScan.Web.Models;

namespace BadgeScan.Web.Services;

/// <summary>
/// Loads badges and old scans from CSV files. Used once, to bring the lists over from the
/// Project-Tracker app, and again whenever a lead has a spreadsheet of new people.
/// </summary>
public sealed class Importer(IBadgeScanStore store)
{
    /// <summary>
    /// Columns: name, team, badge. Team blank means the default team. Badge blank leaves the
    /// person's existing badge alone (so a list of names never clears badges by accident).
    /// </summary>
    public async Task<ImportReport> ImportBadgesAsync(string csv, CancellationToken ct)
    {
        var (rows, columns, problems) = Header(csv, ["name", "team", "badge"]);
        var imported = 0; var updated = 0;
        if (columns is null) return new ImportReport(0, 0, rows.Count, problems);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1;
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            var name = Cell(row, columns, "name").Trim();
            if (name.Length == 0) { problems.Add(new ImportProblem(line, "No name")); continue; }
            if (!TryTeam(Cell(row, columns, "team"), out var team)) { problems.Add(new ImportProblem(line, "Team is not a number")); continue; }
            var badge = BadgeNumber.Normalize(Cell(row, columns, "badge"));
            if (badge is not null && !BadgeNumber.IsValid(badge)) { problems.Add(new ImportProblem(line, $"Badge \"{badge}\" has characters a scanner can't type")); continue; }

            try
            {
                var (_, created) = await store.UpsertBadgeAsync(team, name, badge, ct);
                if (created) imported++; else updated++;
            }
            catch (BadgeTakenException ex)
            {
                problems.Add(new ImportProblem(line, ex.Message));
            }
        }
        return new ImportReport(imported, updated, problems.Count, problems);
    }

    /// <summary>
    /// Columns: name, team, stamp (Th10/08/2026|17:17). Each line becomes one scan with
    /// source "import". A line that was imported before is skipped, not doubled.
    /// </summary>
    public async Task<ImportReport> ImportScansAsync(string csv, CancellationToken ct)
    {
        var (rows, columns, problems) = Header(csv, ["name", "team", "stamp"]);
        var imported = 0; var skipped = 0;
        if (columns is null) return new ImportReport(0, 0, rows.Count, problems);

        var badges = (await store.ListBadgesAsync(null, ct)).ToDictionary(b => (b.Team, b.Name), b => b.BadgeNumber);

        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1;
            var row = rows[i];
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            var name = Cell(row, columns, "name").Trim();
            if (name.Length == 0) { problems.Add(new ImportProblem(line, "No name")); continue; }
            if (!TryTeam(Cell(row, columns, "team"), out var team)) { problems.Add(new ImportProblem(line, "Team is not a number")); continue; }
            var stamp = Cell(row, columns, "stamp").Trim();
            if (!CentralTime.TryParseStamp(stamp, out var instant)) { problems.Add(new ImportProblem(line, $"Stamp \"{stamp}\" is not like Th10/08/2026|17:17")); continue; }

            badges.TryGetValue((team, name), out var badge);
            var saved = await store.InsertScanAsync(new NewScan(badge, team, name, Recognised: true, instant, CentralTime.From(instant), "import"), ct);
            if (saved is null) skipped++; else imported++;
        }
        return new ImportReport(imported, 0, skipped + problems.Count, problems);
    }

    private static (List<string[]> Rows, Dictionary<string, int>? Columns, List<ImportProblem> Problems) Header(string csv, string[] required)
    {
        var rows = Csv.Parse(csv);
        var problems = new List<ImportProblem>();
        if (rows.Count == 0) { problems.Add(new ImportProblem(1, "The file is empty")); return (rows, null, problems); }

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows[0].Length; i++) columns.TryAdd(rows[0][i].Trim(), i);
        var missing = required.Where(c => !columns.ContainsKey(c)).ToList();
        if (missing.Count > 0)
        {
            problems.Add(new ImportProblem(1, $"The first line must name the columns; missing: {string.Join(", ", missing)}"));
            return (rows, null, problems);
        }
        return (rows, columns, problems);
    }

    private static string Cell(string[] row, Dictionary<string, int> columns, string name)
        => columns[name] < row.Length ? row[columns[name]] : "";

    private static bool TryTeam(string text, out int team)
    {
        if (string.IsNullOrWhiteSpace(text)) { team = AppInfo.DefaultTeam; return true; }
        return int.TryParse(text.Trim(), out team) && team > 0;
    }
}
