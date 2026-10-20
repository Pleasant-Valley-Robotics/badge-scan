using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BadgeScan.Web.Tests;

/// <summary>The kiosk's side of things: scanning, through the JSON the kiosk's JavaScript uses.</summary>
public class ScanApiTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private readonly HttpClient _client;

    public ScanApiTests(TestApp app)
    {
        _app = app;
        _client = app.CreateClient();
    }

    private static readonly Regex StampShape = new(@"^(Su|Mo|Tu|We|Th|Fr|Sa)\d{2}/\d{2}/\d{4}\|\d{2}:\d{2}$");

    private async Task<JsonElement> Scan(string badge, int? team = null)
    {
        var response = await _client.PostAsJsonAsync("/api/scans", new { badge, team });
        return await Body(response);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task An_unknown_badge_is_refused_with_a_message()
    {
        var response = await _client.PostAsJsonAsync("/api/scans", new { badge = "0000009999" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await Body(response);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("Badge 9999 not recognised — see a lead", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_empty_scan_is_a_bad_request()
    {
        var response = await _client.PostAsJsonAsync("/api/scans", new { badge = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_form_post_is_not_accepted()
    {
        // Only JSON: a form on another website can't post one of these.
        var response = await _client.PostAsync("/api/scans", new FormUrlEncodedContent([new("badge", "27")]));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task First_scan_checks_in_second_scan_is_already_and_the_next_day_starts_over()
    {
        var created = await _client.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Test Person", badge = "0000000031" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var first = await Scan("31");
        Assert.True(first.GetProperty("ok").GetBoolean());
        Assert.Equal("Test Person", first.GetProperty("name").GetString());
        Assert.True(first.GetProperty("firstOfDay").GetBoolean());
        Assert.False(first.GetProperty("already").GetBoolean());
        Assert.Matches(StampShape, first.GetProperty("stamp").GetString()!);
        Assert.Equal("CDT", first.GetProperty("centralZone").GetString());

        _app.Clock.Advance(TimeSpan.FromMinutes(3));
        var second = await Scan("0000000031");    // the scanner pads; same badge
        Assert.False(second.GetProperty("firstOfDay").GetBoolean());
        Assert.True(second.GetProperty("already").GetBoolean());
        Assert.Equal(first.GetProperty("stamp").GetString(), second.GetProperty("firstStamp").GetString());

        var list = await Body(await _client.GetAsync("/api/scans?team=7196"));
        var names = list.GetProperty("scans").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
        Assert.Equal(2, names.Count(n => n == "Test Person"));

        _app.Clock.Advance(TimeSpan.FromDays(1));
        var tomorrow = await Scan("31");
        Assert.True(tomorrow.GetProperty("firstOfDay").GetBoolean());
    }

    [Fact]
    public async Task Old_scans_import_once()
    {
        await _client.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Old Timer", badge = "77" });
        var csv = new StringContent("name,team,stamp\nOld Timer,7196,Tu09/15/2026|17:05\nOld Timer,,Th09/17/2026|17:12\nNobody,7196,bad stamp\n", Encoding.UTF8, "text/csv");

        var once = await Body(await _client.PostAsync("/api/scans/import", csv));
        Assert.Equal(2, once.GetProperty("imported").GetInt32());
        Assert.Equal(1, once.GetProperty("problems").GetArrayLength());

        var again = await Body(await _client.PostAsync("/api/scans/import", new StringContent("name,team,stamp\nOld Timer,7196,Tu09/15/2026|17:05\n", Encoding.UTF8, "text/csv")));
        Assert.Equal(0, again.GetProperty("imported").GetInt32());
        Assert.Equal(1, again.GetProperty("skipped").GetInt32());

        var day = await Body(await _client.GetAsync("/api/scans?date=2026-09-15&team=7196"));
        var scan = day.GetProperty("scans").EnumerateArray().Single();
        Assert.Equal("77", scan.GetProperty("badge").GetString());
        Assert.Equal("import", scan.GetProperty("source").GetString());
        Assert.True(scan.GetProperty("firstOfDay").GetBoolean());
        Assert.Equal("2026-09-15T17:05:00", scan.GetProperty("scannedCentral").GetString());
        Assert.Equal("2026-09-15T22:05:00+00:00", scan.GetProperty("scannedUtc").GetString());
    }
}

/// <summary>Who holds which badge.</summary>
public class BadgeApiTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly HttpClient _client = app.CreateClient();

    private static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task One_badge_per_person_per_team()
    {
        var a = await Body(await _client.PostAsJsonAsync("/api/badges", new { team = 6093, name = "Person A", badge = "0000000500" }));
        Assert.Equal("500", a.GetProperty("badgeNumber").GetString());

        var clash = await _client.PostAsJsonAsync("/api/badges", new { team = 6093, name = "Person B", badge = "500" });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal("Badge 500 already belongs to Person A.", (await Body(clash)).GetProperty("error").GetString());

        // The same number on another team is a different badge.
        var other = await _client.PostAsJsonAsync("/api/badges", new { team = 9082, name = "Person C", badge = "500" });
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);

        // Clear A's badge; now B may have it.
        var cleared = await Body(await _client.PutAsJsonAsync($"/api/badges/{a.GetProperty("id").GetInt64()}", new { badge = (string?)null }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("badgeNumber").ValueKind);
        var b = await _client.PostAsJsonAsync("/api/badges", new { team = 6093, name = "Person B", badge = "500" });
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);   // the refused attempt added nobody, so B is new
    }

    [Fact]
    public async Task Adding_a_known_name_keeps_their_badge()
    {
        await _client.PostAsJsonAsync("/api/badges", new { team = 38350, name = "Keeper", badge = "12" });
        var again = await Body(await _client.PostAsJsonAsync("/api/badges", new { team = 38350, name = "Keeper" }));
        Assert.Equal("12", again.GetProperty("badgeNumber").GetString());
    }

    [Fact]
    public async Task People_import_from_csv()
    {
        var csv = new StringContent("name,team,badge\nImported One,38350,0000000900\nImported Two,38350,\n,38350,901\nImported One,38350,902\n", Encoding.UTF8, "text/csv");
        var report = await Body(await _client.PostAsync("/api/badges/import", csv));

        Assert.Equal(2, report.GetProperty("imported").GetInt32());
        Assert.Equal(1, report.GetProperty("updated").GetInt32());
        Assert.Equal(1, report.GetProperty("skipped").GetInt32());

        var people = await Body(await _client.GetAsync("/api/badges?team=38350"));
        var one = people.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Imported One");
        Assert.Equal("902", one.GetProperty("badgeNumber").GetString());
    }

    [Fact]
    public async Task Removing_a_person()
    {
        var p = await Body(await _client.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Leaving", badge = "4444" }));
        var id = p.GetProperty("id").GetInt64();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/badges/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/badges/{id}")).StatusCode);
    }
}

/// <summary>The live list: a scan saved by one kiosk shows up on every other within a moment.</summary>
public class StreamTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task A_new_scan_arrives_on_the_stream()
    {
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Streamer", badge = "8080" });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync("/api/scans/stream", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));

        await client.PostAsJsonAsync("/api/scans", new { badge = "8080" }, timeout.Token);

        string? line; var sawEvent = false; string? data = null;
        while ((line = await reader.ReadLineAsync(timeout.Token)) is not null)
        {
            if (line == "event: scan") sawEvent = true;
            if (sawEvent && line.StartsWith("data: ")) { data = line[6..]; break; }
        }
        Assert.NotNull(data);
        var scan = JsonDocument.Parse(data).RootElement;
        Assert.Equal("Streamer", scan.GetProperty("name").GetString());
        Assert.Equal("8080", scan.GetProperty("badge").GetString());
    }

    [Fact]
    public async Task A_reconnecting_kiosk_gets_what_it_missed()
    {
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Missed", badge = "9090" });
        var saved = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/scans", new { badge = "9090" })).Content.ReadAsStringAsync()).RootElement;
        var id = saved.GetProperty("id").GetInt64();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/scans/stream");
        request.Headers.Add("Last-Event-ID", (id - 1).ToString());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));

        string? line; var ids = new List<long>();
        while ((line = await reader.ReadLineAsync(timeout.Token)) is not null)
        {
            if (line.StartsWith("id: ")) ids.Add(long.Parse(line[4..]));
            if (ids.Contains(id)) break;
        }
        Assert.Contains(id, ids);
    }
}
