using System.Net;
using System.Text.RegularExpressions;
using BadgeScan.Web.Models;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BadgeScan.Web.Tests;

/// <summary>
/// Starts the site in memory for the tests, with the in-memory store (never a real
/// database, even if PGHOST happens to be set) and a clock the tests control.
/// The clock starts at 2026-10-08 22:17 UTC, which is Thursday 17:17 CDT in Central time.
/// </summary>
public class TestApp : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Start = new(2026, 10, 8, 22, 17, 0, TimeSpan.Zero);
    public const string Password = "correct horse battery";

    public FakeTimeProvider Clock { get; } = new(Start);
    private int _accounts;

    /// <summary>Sign-in attempts allowed per minute from one address. The tests all come from the same one.</summary>
    protected virtual int SignInsPerMinute => 100_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("BADGESCAN_STORE", "memory");
        builder.UseSetting("BADGESCAN_SIGNIN_PER_MINUTE", SignInsPerMinute.ToString());
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            // The test's HttpClient keeps cookies by the real date. A cookie stamped to expire
            // on the test clock's (past) date would be thrown away at once, so the cookie's own
            // dates follow the real clock. Meetings, lockouts and scans still follow Clock.
            services.PostConfigure<CookieAuthenticationOptions>(SignIns.CookieScheme, o => o.TimeProvider = TimeProvider.System);
        });
    }

    /// <summary>A browser that keeps its cookies and shows redirects instead of following them.</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>A new account that has already chosen its password (TestApp.Password).</summary>
    public async Task<Account> NewAccountAsync(string name = "Test Mentor")
    {
        var service = Services.GetRequiredService<AccountService>();
        var (account, temporary) = await service.CreateAsync($"tester{Interlocked.Increment(ref _accounts)}", name, "mentor", default);
        Assert.Null(await service.ChangePasswordAsync(account, temporary, Password, Password, default));
        return account;
    }

    /// <summary>A browser signed in as a new lead, coach or mentor.</summary>
    public async Task<(HttpClient Client, Account Account)> StaffAsync(string name = "Test Mentor")
    {
        var account = await NewAccountAsync(name);
        var client = Browser();
        var response = await PostFormAsync(client, "/login", new() { ["Username"] = account.Username, ["Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        return (client, account);
    }

    /// <summary>A browser someone started a meeting on: the kiosk.</summary>
    public async Task<(HttpClient Client, Account OpenedBy)> KioskAsync(string openedBy = "Kiosk Opener")
    {
        var (client, account) = await StaffAsync(openedBy);
        var response = await PostFormAsync(client, "/", new() { ["hours"] = "3" }, "/?handler=Start");
        Assert.Equal("/kiosk", response.Headers.Location?.OriginalString);
        return (client, account);
    }

    /// <summary>
    /// Submits a form the way a browser does: opens the page it's on for the form's hidden
    /// anti-forgery token (every page has one, in the Sign out form or its own), then posts.
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string page, Dictionary<string, string> fields, string? action = null)
    {
        var html = await (await client.GetAsync(page)).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(token.Success, $"No form token on {page}");
        fields["__RequestVerificationToken"] = token.Groups[1].Value;
        return await client.PostAsync(action ?? page, new FormUrlEncodedContent(fields));
    }
}

/// <summary>The same site, allowing only three sign-in attempts a minute.</summary>
public sealed class StrictTestApp : TestApp
{
    protected override int SignInsPerMinute => 3;
}
