using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BadgeScan.Web.Controllers.Api;

/// <summary>Who holds which badge, as JSON. The Badges page's JavaScript talks to this.</summary>
[ApiController]
[Route("api/badges")]
public sealed class BadgesApiController(IBadgeScanStore store, Importer importer) : ControllerBase
{
    /// <summary>Everyone and their badge. Staff, or another program with a data key (Project-Tracker).</summary>
    [HttpGet]
    [Authorize(Policy = SignIns.Policies.ReadScans)]
    public async Task<IActionResult> List([FromQuery] string? team, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(team)) return Ok(await store.ListBadgesAsync(null, ct));
        return Groups.TryParse(team, out var code)
            ? Ok(await store.ListBadgesAsync(code, ct))
            : BadRequest(new { error = $"team is {Groups.Expected}" });
    }

    /// <summary>Add a person, with or without a badge. Body: { "team": 7196, "name": "Jane D.", "badge": "27" }, or "role": "mentor" instead of a team.</summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Add([FromBody] BadgeRequest request, CancellationToken ct)
    {
        var name = (request.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { error = "A name is needed" });
        var team = request.Team ?? AppInfo.DefaultTeam;
        if (!string.IsNullOrWhiteSpace(request.Role) && !Groups.TryParse(request.Role, out team))
            return BadRequest(new { error = "The role is mentor, coach or advisor" });
        if (!Groups.IsValid(team)) return BadRequest(new { error = $"Who they are is {Groups.Expected}" });
        var badge = BadgeNumber.Normalize(request.Badge);
        if (badge is not null && !BadgeNumber.IsValid(badge)) return BadRequest(new { error = "A badge is letters and digits only" });
        try
        {
            var (saved, created) = await store.UpsertBadgeAsync(team, name, badge, ct);
            return created ? Created($"/api/badges/{saved.Id}", saved) : Ok(saved);
        }
        catch (BadgeTakenException ex)
        {
            return Conflict(new { error = ex.Message, badge = ex.BadgeNumber, owner = ex.OwnerName });
        }
    }

    /// <summary>Assign, change or clear one person's badge. Body: { "badge": "27" } or { "badge": null }</summary>
    [HttpPut("{id:long}")]
    [Consumes("application/json")]
    public async Task<IActionResult> SetBadge(long id, [FromBody] BadgeNumberRequest request, CancellationToken ct)
    {
        var badge = BadgeNumber.Normalize(request.Badge);
        if (badge is not null && !BadgeNumber.IsValid(badge)) return BadRequest(new { error = "A badge is letters and digits only" });
        try
        {
            var saved = await store.SetBadgeNumberAsync(id, badge, ct);
            return saved is null ? NotFound(new { error = "No such person" }) : Ok(saved);
        }
        catch (BadgeTakenException ex)
        {
            return Conflict(new { error = ex.Message, badge = ex.BadgeNumber, owner = ex.OwnerName });
        }
    }

    /// <summary>Remove a person from the list. Their past scans keep their name.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => await store.DeleteBadgeAsync(id, ct) ? NoContent() : NotFound(new { error = "No such person" });

    /// <summary>People from a CSV with columns name, team, badge. Body is the file itself.</summary>
    [HttpPost("import")]
    [Consumes("text/csv", "text/plain")]
    public async Task<IActionResult> Import(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        return Ok(await importer.ImportBadgesAsync(await reader.ReadToEndAsync(ct), ct));
    }
}
