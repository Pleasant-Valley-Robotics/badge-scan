using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BadgeScan.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BadgeScan.Web.Tests;

/// <summary>Signing in: nothing without it, wrong passwords, lockouts, temporary passwords, turning accounts off.</summary>
public class SignInTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static Task<HttpResponseMessage> SignIn(HttpClient client, string username, string password)
        => TestApp.PostFormAsync(client, "/login", new() { ["Username"] = username, ["Password"] = password });

    [Fact]
    public async Task Without_signing_in_there_is_only_the_sign_in_page()
    {
        var stranger = app.Browser();

        var home = await stranger.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.StartsWith("http://localhost/login", home.Headers.Location!.ToString());

        foreach (var page in new[] { "/badges", "/scans", "/import", "/accounts", "/kiosk", "/scans.csv" })
            Assert.Equal(HttpStatusCode.Redirect, (await stranger.GetAsync(page)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.PostAsJsonAsync("/api/scans", new { badge = "27" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/scans")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/badges")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/scans/since")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/css/site.css")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_or_name_says_the_same_thing()
    {
        var account = await app.NewAccountAsync();

        var wrongPassword = await (await SignIn(app.Browser(), account.Username, "not the password")).Content.ReadAsStringAsync();
        var wrongName = await (await SignIn(app.Browser(), "nobody-here", TestApp.Password)).Content.ReadAsStringAsync();

        Assert.Contains("Wrong username or password.", wrongPassword);
        Assert.Contains("Wrong username or password.", wrongName);
    }

    [Fact]
    public async Task Usernames_are_not_case_sensitive()
    {
        var account = await app.NewAccountAsync();
        var response = await SignIn(app.Browser(), account.Username.ToUpperInvariant(), TestApp.Password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_against_the_right_one()
    {
        var account = await app.NewAccountAsync();
        for (var i = 0; i < AccountService.MaxFailedSignIns; i++)
            await SignIn(app.Browser(), account.Username, "guess " + i);

        var locked = await SignIn(app.Browser(), account.Username, TestApp.Password);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("locked until", await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_temporary_password_must_be_replaced_before_anything_else()
    {
        var (account, temporary) = await app.Services.GetRequiredService<AccountService>().CreateAsync("newlead", "New Lead", "lead", default);
        var browser = app.Browser();

        var signIn = await SignIn(browser, "newlead", temporary);
        Assert.Equal("/account/password", signIn.Headers.Location?.OriginalString);
        Assert.Equal("/account/password", (await browser.GetAsync("/badges")).Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/badges")).StatusCode);

        var tooShort = await TestApp.PostFormAsync(browser, "/account/password",
            new() { ["Current"] = temporary, ["Chosen"] = "short", ["Repeated"] = "short" });
        Assert.Contains("at least 10 characters", await tooShort.Content.ReadAsStringAsync());

        var saved = await TestApp.PostFormAsync(browser, "/account/password",
            new() { ["Current"] = temporary, ["Chosen"] = "my own long password", ["Repeated"] = "my own long password" });
        Assert.Equal("/", saved.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/badges")).StatusCode);

        // The temporary one is gone.
        Assert.Contains("Wrong username or password.", await (await SignIn(app.Browser(), "newlead", temporary)).Content.ReadAsStringAsync());
        Assert.Equal(account.Id, (await app.Services.GetRequiredService<BadgeScan.Web.Data.IAccountStore>().FindAccountAsync("newlead", default))!.Id);
    }

    [Fact]
    public async Task Accounts_page_adds_someone_who_can_then_sign_in()
    {
        var (mentor, _) = await app.StaffAsync();

        var added = await TestApp.PostFormAsync(mentor, "/accounts",
            new() { ["displayName"] = "Casey Coach", ["username"] = "ccoach", ["title"] = "coach" }, "/accounts?handler=Add");
        var html = await added.Content.ReadAsStringAsync();
        var temporary = Regex.Match(html, "<code class=\"secret\">([a-z0-9-]+)</code>").Groups[1].Value;
        Assert.Matches("^[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}$", temporary);

        var again = await TestApp.PostFormAsync(mentor, "/accounts",
            new() { ["displayName"] = "Someone Else", ["username"] = "ccoach", ["title"] = "lead" }, "/accounts?handler=Add");
        Assert.Contains("already taken", await again.Content.ReadAsStringAsync());

        Assert.Equal("/account/password", (await SignIn(app.Browser(), "ccoach", temporary)).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Turning_an_account_off_signs_it_out_at_once()
    {
        var (mentor, _) = await app.StaffAsync();
        var (other, otherAccount) = await app.StaffAsync("Leaving Mentor");
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/")).StatusCode);

        await TestApp.PostFormAsync(mentor, "/accounts", new(), $"/accounts?handler=Disable&id={otherAccount.Id}&off=True");

        Assert.Equal(HttpStatusCode.Redirect, (await other.GetAsync("/")).StatusCode);
        Assert.Contains("Wrong username or password.", await (await SignIn(app.Browser(), otherAccount.Username, TestApp.Password)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Nobody_can_turn_off_their_own_account()
    {
        var (mentor, me) = await app.StaffAsync();
        var response = await TestApp.PostFormAsync(mentor, "/accounts", new(), $"/accounts?handler=Disable&id={me.Id}&off=True");
        Assert.Contains("can&#x27;t turn off your own account", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await mentor.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task A_new_password_from_someone_else_signs_the_person_out()
    {
        var (mentor, _) = await app.StaffAsync();
        var (forgetful, account) = await app.StaffAsync("Forgetful Mentor");

        await TestApp.PostFormAsync(mentor, "/accounts", new(), $"/accounts?handler=Reset&id={account.Id}");

        Assert.Equal(HttpStatusCode.Redirect, (await forgetful.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task Cookies_are_secure_behind_https()
    {
        // On the server, Cloudflare and Caddy say the visitor used https with X-Forwarded-Proto.
        // Cookies by hand: a client that keeps cookies hides the Set-Cookie headers.
        var account = await app.NewAccountAsync();
        var browser = app.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
        browser.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var page = await browser.GetAsync("/login");
        var pageCookies = page.Headers.GetValues("Set-Cookie").ToList();
        var token = Regex.Match(await page.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var post = new HttpRequestMessage(HttpMethod.Post, "/login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = account.Username, ["Password"] = TestApp.Password, ["__RequestVerificationToken"] = token,
            }),
        };
        post.Headers.Add("Cookie", string.Join("; ", pageCookies.Select(c => c.Split(';')[0])));
        var signIn = await browser.SendAsync(post);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);

        var cookies = pageCookies.Concat(signIn.Headers.GetValues("Set-Cookie")).ToList();
        Assert.Contains(cookies, c => c.StartsWith("pv_attendance="));
        Assert.All(cookies, c => Assert.Contains("secure", c, StringComparison.OrdinalIgnoreCase));
        Assert.All(cookies, c => Assert.Contains("httponly", c, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Signing_out_ends_the_sign_in()
    {
        var (mentor, _) = await app.StaffAsync();
        var response = await TestApp.PostFormAsync(mentor, "/", new(), "/logout");
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await mentor.GetAsync("/")).StatusCode);
    }
}

/// <summary>Too many sign-in attempts from one place are turned away for a minute.</summary>
public class SignInLimitTests(StrictTestApp app) : IClassFixture<StrictTestApp>
{
    [Fact]
    public async Task The_fourth_attempt_in_a_minute_is_refused()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await TestApp.PostFormAsync(app.Browser(), "/login", new() { ["Username"] = "someone", ["Password"] = "guess" })).StatusCode);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }
}

/// <summary>A meeting: the kiosk it makes, what it can and can't do, and how it ends.</summary>
public class MeetingTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task The_kiosk_scans_any_team_and_says_who_opened_it()
    {
        var (mentor, _) = await app.StaffAsync();
        await mentor.PostAsJsonAsync("/api/badges", new { team = 6093, name = "Kiosk Scanner", badge = "6100" });
        await mentor.PostAsJsonAsync("/api/badges", new { team = 9082, name = "Other Team Scanner", badge = "6200" });
        var (kiosk, opener) = await app.KioskAsync("Morgan Mentor");

        var first = await Body(await kiosk.PostAsJsonAsync("/api/scans", new { badge = "6100" }));
        var second = await Body(await kiosk.PostAsJsonAsync("/api/scans", new { badge = "0000006200" }));

        Assert.Equal("Kiosk Scanner", first.GetProperty("name").GetString());
        Assert.Equal(6093, first.GetProperty("team").GetInt32());
        Assert.Equal("6100", first.GetProperty("badge").GetString());
        Assert.Equal("Thursday, October 8, 2026", first.GetProperty("dateText").GetString());
        Assert.Equal("5:17 PM CDT", first.GetProperty("timeText").GetString());
        Assert.Equal("Morgan Mentor", first.GetProperty("openedBy").GetString());
        Assert.Equal(JsonValueKind.Number, first.GetProperty("meetingId").ValueKind);
        Assert.Equal(9082, second.GetProperty("team").GetInt32());
    }

    [Fact]
    public async Task The_kiosk_cannot_open_anything_else()
    {
        var (kiosk, _) = await app.KioskAsync();

        foreach (var page in new[] { "/", "/badges", "/scans", "/import", "/accounts", "/scans.csv" })
            Assert.Equal("/kiosk", (await kiosk.GetAsync(page)).Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.Forbidden, (await kiosk.GetAsync("/api/badges")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kiosk.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Sneaky", badge = "1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kiosk.GetAsync("/api/scans/since")).StatusCode);
    }

    [Fact]
    public async Task The_kiosk_sees_only_todays_last_ten()
    {
        var (mentor, _) = await app.StaffAsync();
        await mentor.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Many Scans", badge = "7700" });
        var (kiosk, _) = await app.KioskAsync();
        for (var i = 0; i < 12; i++) await kiosk.PostAsJsonAsync("/api/scans", new { badge = "7700" });

        var list = await Body(await kiosk.GetAsync("/api/scans?date=2026-01-01&to=2026-12-31&limit=500"));

        Assert.Equal("2026-10-08", list.GetProperty("date").GetString());
        Assert.Equal(10, list.GetProperty("scans").GetArrayLength());
    }

    [Fact]
    public async Task Ending_the_meeting_needs_a_password_and_signs_that_person_in()
    {
        var (mentor, _) = await app.StaffAsync();
        await mentor.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Ender Test", badge = "8800" });
        var (kiosk, opener) = await app.KioskAsync();
        await kiosk.PostAsJsonAsync("/api/scans", new { badge = "8800" });

        var wrong = await TestApp.PostFormAsync(kiosk, "/kiosk/end", new() { ["Username"] = opener.Username, ["Password"] = "a teammate's guess" });
        Assert.Contains("Wrong username or password.", await wrong.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await kiosk.GetAsync("/kiosk")).StatusCode);

        var ended = await TestApp.PostFormAsync(kiosk, "/kiosk/end", new() { ["Username"] = opener.Username, ["Password"] = TestApp.Password });
        Assert.Equal("/", ended.Headers.Location?.OriginalString);

        var home = await (await kiosk.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("Meeting ended: 1 checked in, 1 scans in all.", home);
        Assert.Equal(HttpStatusCode.OK, (await kiosk.GetAsync("/badges")).StatusCode);
        Assert.Equal("/", (await kiosk.GetAsync("/kiosk")).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Someone_else_can_end_a_kiosk_left_running()
    {
        var (kiosk, _) = await app.KioskAsync();
        var (mentor, _) = await app.StaffAsync();
        var start = await (await mentor.GetAsync("/")).Content.ReadAsStringAsync();
        var id = Regex.Matches(start, "id=(\\d+)&amp;handler=End").Last().Groups[1].Value;

        await TestApp.PostFormAsync(mentor, "/", new(), $"/?handler=End&id={id}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await kiosk.PostAsJsonAsync("/api/scans", new { badge = "1" })).StatusCode);
        Assert.StartsWith("http://localhost/login", (await kiosk.GetAsync("/kiosk")).Headers.Location?.ToString());
    }
}

/// <summary>A meeting runs out on its own. Its own site, because it moves the clock.</summary>
public class MeetingTimeTests(TestApp app) : IClassFixture<TestApp>
{
    [Fact]
    public async Task A_meeting_stops_taking_scans_when_its_time_is_up()
    {
        var (kiosk, _) = await app.KioskAsync();
        Assert.Equal(HttpStatusCode.OK, (await kiosk.GetAsync("/kiosk")).StatusCode);

        app.Clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await kiosk.PostAsJsonAsync("/api/scans", new { badge = "1" })).StatusCode);
        var page = await kiosk.GetAsync("/kiosk");
        Assert.Equal("http://localhost/login?ReturnUrl=%2Fkiosk", page.Headers.Location?.ToString());
        Assert.Contains("The meeting on this screen has ended", await (await kiosk.GetAsync(page.Headers.Location)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_lock_wears_off()
    {
        var account = await app.NewAccountAsync();
        for (var i = 0; i < AccountService.MaxFailedSignIns; i++)
            await TestApp.PostFormAsync(app.Browser(), "/login", new() { ["Username"] = account.Username, ["Password"] = "guess" });

        app.Clock.Advance(AccountService.LockFor + TimeSpan.FromMinutes(1));

        var response = await TestApp.PostFormAsync(app.Browser(), "/login", new() { ["Username"] = account.Username, ["Password"] = TestApp.Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}

/// <summary>Data keys: how Project-Tracker reads the scans without anyone's password.</summary>
public class DataKeyTests(TestApp app) : IClassFixture<TestApp>
{
    private static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(HttpClient Mentor, string Key)> MakeKey()
    {
        var (mentor, _) = await app.StaffAsync();
        var page = await TestApp.PostFormAsync(mentor, "/accounts", new() { ["label"] = "Project-Tracker" }, "/accounts?handler=CreateKey");
        var key = Regex.Match(await page.Content.ReadAsStringAsync(), "<code class=\"secret\">(bsk_[A-Za-z0-9_-]+)</code>").Groups[1].Value;
        Assert.NotEmpty(key);
        return (mentor, key);
    }

    private HttpClient Program(string key)
    {
        var client = app.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    [Fact]
    public async Task A_key_reads_scans_page_by_page()
    {
        var (mentor, key) = await MakeKey();
        await mentor.PostAsJsonAsync("/api/badges", new { team = 7196, name = "Synced Person", badge = "5150" });
        await mentor.PostAsJsonAsync("/api/scans", new { badge = "5150" });
        await mentor.PostAsJsonAsync("/api/scans", new { badge = "5150" });
        var program = Program(key);

        var first = await Body(await program.GetAsync("/api/scans/since?after=0&limit=1"));
        Assert.Equal(1, first.GetProperty("scans").GetArrayLength());
        var next = first.GetProperty("next").GetInt64();

        var rest = await Body(await program.GetAsync($"/api/scans/since?after={next}"));
        var ids = rest.GetProperty("scans").EnumerateArray().Select(s => s.GetProperty("id").GetInt64()).ToList();
        Assert.All(ids, id => Assert.True(id > next));
        Assert.Contains(rest.GetProperty("scans").EnumerateArray(), s => s.GetProperty("name").GetString() == "Synced Person");

        var done = await Body(await program.GetAsync($"/api/scans/since?after={rest.GetProperty("next").GetInt64()}"));
        Assert.Equal(0, done.GetProperty("scans").GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await program.GetAsync("/scans.csv?date=2026-10-08")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await program.GetAsync("/api/scans?date=2026-10-08")).StatusCode);
    }

    [Fact]
    public async Task A_key_can_only_read()
    {
        var (_, key) = await MakeKey();
        var program = Program(key);

        Assert.Equal(HttpStatusCode.Unauthorized, (await program.PostAsJsonAsync("/api/scans", new { badge = "5150" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await program.GetAsync("/api/badges")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await program.GetAsync("/api/scans/stream")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await program.GetAsync("/accounts")).StatusCode);
    }

    [Fact]
    public async Task A_made_up_or_revoked_key_reads_nothing()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Program("bsk_made-up").GetAsync("/api/scans/since")).StatusCode);

        var (mentor, key) = await MakeKey();
        Assert.Equal(HttpStatusCode.OK, (await Program(key).GetAsync("/api/scans/since")).StatusCode);

        var accounts = await (await mentor.GetAsync("/accounts")).Content.ReadAsStringAsync();
        var id = Regex.Matches(accounts, "id=(\\d+)&amp;handler=RevokeKey").First().Groups[1].Value;
        await TestApp.PostFormAsync(mentor, "/accounts", new(), $"/accounts?handler=RevokeKey&id={id}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Program(key).GetAsync("/api/scans/since")).StatusCode);
    }
}
