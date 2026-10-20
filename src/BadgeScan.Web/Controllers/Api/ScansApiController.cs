using System.Text.Json;
using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace BadgeScan.Web.Controllers.Api;

/// <summary>
/// The scans, as JSON. The kiosk's JavaScript talks to this; so can anything else that
/// wants the scans, such as the program that will carry them into the attendance app.
/// </summary>
[ApiController]
[Route("api/scans")]
public sealed class ScansApiController(IBadgeScanStore store, ScanRecorder recorder, ScanBroadcaster broadcaster, Importer importer, TimeProvider clock) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A badge was scanned. Body: { "badge": "0000000027", "team": 7196 } (team is optional).</summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Record([FromBody] ScanRequest request, CancellationToken ct)
    {
        var outcome = await recorder.RecordAsync(request.Badge, request.Team, ct);
        return outcome switch
        {
            ScanOutcome.Saved s => Ok(s.Result),
            ScanOutcome.Empty => BadRequest(new { ok = false, error = "Empty scan" }),
            ScanOutcome.NotRecognised n => NotFound(new { ok = false, error = $"Badge {n.Badge} not recognised — see a lead", badge = n.Badge }),
            ScanOutcome.Ambiguous a => Conflict(new { ok = false, error = $"Badge {a.Badge} belongs to someone on more than one team — open the kiosk with ?team=", badge = a.Badge }),
            _ => StatusCode(500),
        };
    }

    /// <summary>Scans for one Central date (default today) or a range, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly? date, [FromQuery] DateOnly? to, [FromQuery] int? team, [FromQuery] int? limit, CancellationToken ct)
    {
        var from = date ?? CentralTime.Today(clock);
        var until = to ?? from;
        if (until < from) return BadRequest(new { ok = false, error = "'to' is before 'date'" });
        var scans = await store.ScansForDatesAsync(from, until, team, limit, ct);
        return Ok(new { date = from, to = until, team, scans });
    }

    /// <summary>
    /// Server-sent events: every new scan as it happens. The kiosk opens this once and keeps
    /// listening. A comment line every 15 seconds keeps the connection alive through
    /// Cloudflare, and a browser that reconnects sends Last-Event-ID so it gets what it missed.
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        Response.StatusCode = 200;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // Subscribe before replaying, so nothing can slip between the two.
        using var subscription = broadcaster.Subscribe();
        long lastSent = long.TryParse(Request.Headers["Last-Event-ID"], out var id) ? id : 0;

        await Write("retry: 3000\n\n", ct);
        if (lastSent > 0)
        {
            foreach (var scan in await store.ScansAfterAsync(lastSent, CentralTime.Today(clock), ct))
                lastSent = await WriteEvent(scan, ct);
        }

        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var read = subscription.Reader.WaitToReadAsync(ct).AsTask();
        var tick = heartbeat.WaitForNextTickAsync(ct).AsTask();
        try
        {
            while (true)
            {
                var done = await Task.WhenAny(read, tick);
                if (done == read)
                {
                    if (!await read) break;
                    while (subscription.Reader.TryRead(out var scan))
                        if (scan.Id > lastSent) lastSent = await WriteEvent(scan, ct);
                    read = subscription.Reader.WaitToReadAsync(ct).AsTask();
                }
                else
                {
                    if (!await tick) break;
                    await Write(": ping\n\n", ct);
                    tick = heartbeat.WaitForNextTickAsync(ct).AsTask();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The browser closed the tab. Nothing to do.
        }
    }

    /// <summary>Old scans from a CSV with columns name, team, stamp. Body is the file itself.</summary>
    [HttpPost("import")]
    [Consumes("text/csv", "text/plain")]
    public async Task<IActionResult> Import(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var report = await importer.ImportScansAsync(await reader.ReadToEndAsync(ct), ct);
        return Ok(report);
    }

    /// <summary>The same list as a spreadsheet: /scans.csv?date=2026-10-08&to=2026-10-31&team=7196</summary>
    [HttpGet("/scans.csv")]
    public async Task<IActionResult> Csv([FromQuery] DateOnly? date, [FromQuery] DateOnly? to, [FromQuery] int? team, CancellationToken ct)
    {
        var from = date ?? CentralTime.Today(clock);
        var until = to ?? from;
        var scans = await store.ScansForDatesAsync(from, until, team, null, ct);
        var lines = new List<string> { Services.Csv.Line("id", "stamp", "name", "team", "badge", "first_of_day", "recognised", "central_zone", "scanned_central", "scanned_utc", "source") };
        foreach (var s in scans.Reverse())
        {
            lines.Add(Services.Csv.Line(s.Id.ToString(), s.Stamp, s.Name, s.Team?.ToString(), s.BadgeNumber,
                s.FirstOfDay ? "true" : "false", s.Recognised ? "true" : "false", s.CentralZone,
                s.ScannedCentral.ToString("yyyy-MM-dd HH:mm:ss"), s.ScannedUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss'Z'"), s.Source));
        }
        var name = until == from ? $"badge-scans-{from:yyyy-MM-dd}.csv" : $"badge-scans-{from:yyyy-MM-dd}-to-{until:yyyy-MM-dd}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n"), "text/csv; charset=utf-8", name);
    }

    private async Task<long> WriteEvent(ScanResult scan, CancellationToken ct)
    {
        await Write($"id: {scan.Id}\nevent: scan\ndata: {JsonSerializer.Serialize(scan, Json)}\n\n", ct);
        return scan.Id;
    }

    private async Task Write(string text, CancellationToken ct)
    {
        await Response.WriteAsync(text, ct);
        await Response.Body.FlushAsync(ct);
    }
}
