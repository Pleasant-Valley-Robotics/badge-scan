namespace BadgeScan.Web.Models;

/// <summary>What the kiosk sends: the badge as typed or scanned, and optionally which team's badge it is.</summary>
public sealed record ScanRequest(string? Badge, int? Team);

/// <summary>Add a person, optionally with their badge.</summary>
public sealed record BadgeRequest(int? Team, string? Name, string? Badge);

/// <summary>Assign, change or (with null) clear one person's badge.</summary>
public sealed record BadgeNumberRequest(string? Badge);

/// <summary>What an import did, line by line where something was skipped.</summary>
public sealed record ImportReport(int Imported, int Updated, int Skipped, IReadOnlyList<ImportProblem> Problems);

public sealed record ImportProblem(int Line, string Reason);
