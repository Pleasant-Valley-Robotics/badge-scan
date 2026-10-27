using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BadgeScan.Web.Tests;

/// <summary>
/// What Project-Tracker relies on (docs/project-tracker.md). If one of these fails, a
/// change here would break its attendance sync: keep the field, or change both sides
/// together and update the guide.
/// </summary>
public class ProjectTrackerContractTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> KeyAsync()
    {
        var (mentor, _) = await app.StaffAsync();
        var page = await TestApp.PostFormAsync(mentor, "/accounts", new() { ["label"] = "Project-Tracker" }, "/accounts?handler=CreateKey");
        var key = Regex.Match(await page.Content.ReadAsStringAsync(), "<code class=\"secret\">(bsk_[A-Za-z0-9_-]+)</code>").Groups[1].Value;
        var client = app.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    private static void HasFields(JsonElement item, params string[] fields)
    {
        foreach (var field in fields)
            Assert.True(item.TryGetProperty(field, out _), $"Project-Tracker reads \"{field}\", which is missing from {item}");
    }

    [Fact]
    public async Task The_key_says_who_it_is()
    {
        var me = await Body(await (await KeyAsync()).GetAsync("/api/me"));

        Assert.Equal("program", me.GetProperty("kind").GetString());
        Assert.Equal("Project-Tracker", me.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Scans_since_has_the_cursor_and_every_field_the_sync_reads()
    {
        var (mentor, _) = await app.StaffAsync();
        await mentor.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Contract Kid", badge = "6101" });
        await mentor.PostAsJsonAsync("/api/badges", new { role = "mentor", name = "Contract Mentor", badge = "6102" });
        var (kiosk, _) = await app.KioskAsync();
        await kiosk.PostAsJsonAsync("/api/scans", new { badge = "6101" });
        await kiosk.PostAsJsonAsync("/api/scans", new { badge = "6102" });
        await kiosk.PostAsJsonAsync("/api/scans", new { badge = "6199" }); // nobody's

        var page = await Body(await (await KeyAsync()).GetAsync("/api/scans/since?after=0&limit=1000"));
        HasFields(page, "after", "next", "scans");
        var scans = page.GetProperty("scans").EnumerateArray().ToList();
        Assert.Equal(scans[^1].GetProperty("id").GetInt64(), page.GetProperty("next").GetInt64());

        var kid = scans.Last(s => s.GetProperty("name").GetString() == "Contract Kid");
        HasFields(kid, "id", "badge", "team", "role", "group", "name", "recognised", "firstOfDay", "stamp",
            "scannedUtc", "scannedCentral", "centralDate", "source", "meetingId", "openedBy");
        Assert.Equal(7196, kid.GetProperty("team").GetInt32());
        Assert.Equal(JsonValueKind.Null, kid.GetProperty("role").ValueKind);
        Assert.Equal("6101", kid.GetProperty("badge").GetString());
        Assert.Matches(@"^[A-Z][a-z]\d\d/\d\d/\d{4}\|\d\d:\d\d$", kid.GetProperty("stamp").GetString());
        Assert.Matches(@"^\d{4}-\d\d-\d\d$", kid.GetProperty("centralDate").GetString());
        Assert.Equal(JsonValueKind.Number, kid.GetProperty("meetingId").ValueKind);

        var mentorScan = scans.Last(s => s.GetProperty("name").GetString() == "Contract Mentor");
        Assert.Equal(JsonValueKind.Null, mentorScan.GetProperty("team").ValueKind);
        Assert.Equal("mentor", mentorScan.GetProperty("role").GetString());

        var unknown = scans.Last(s => s.GetProperty("badge").GetString() == "6199");
        Assert.False(unknown.GetProperty("recognised").GetBoolean());
    }

    [Fact]
    public async Task The_badge_list_has_the_fields_the_sync_reads()
    {
        var (mentor, _) = await app.StaffAsync();
        await mentor.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Roster Kid", badge = "6201" });

        var people = await Body(await (await KeyAsync()).GetAsync("/api/badges"));
        var kid = people.EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Roster Kid");
        HasFields(kid, "id", "name", "team", "role", "group", "badgeNumber");
        Assert.Equal("6201", kid.GetProperty("badgeNumber").GetString());
    }
}
