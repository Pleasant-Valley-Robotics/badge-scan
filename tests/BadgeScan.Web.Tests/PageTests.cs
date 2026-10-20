using System.Net;
using System.Text.RegularExpressions;

namespace BadgeScan.Web.Tests;

/// <summary>
/// These tests start the whole site in memory and visit its pages, the way a browser
/// would. GitHub runs them on every pull request. Run them yourself with:  dotnet test
/// </summary>
public class PageTests(TestApp app) : IClassFixture<TestApp>
{
    private readonly HttpClient _client = app.CreateClient();

    private async Task<string> HomePage()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Regex.Replace(await response.Content.ReadAsStringAsync(), "<!--.*?-->", "", RegexOptions.Singleline);
    }

    [Fact]
    public async Task Kiosk_shows_the_scan_box_and_menu()
    {
        var html = await HomePage();

        Assert.Contains("id=\"badge\"", html);
        Assert.Contains("Scan your badge to check in", html);
        Assert.Contains("<nav", html);
    }

    [Fact]
    public async Task Every_menu_link_works()
    {
        var nav = Regex.Match(await HomePage(), "<nav.*?</nav>", RegexOptions.Singleline).Value;
        var links = Regex.Matches(nav, "href=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(links);

        foreach (var href in links.Where(h => h.StartsWith('/')))
        {
            var response = await _client.GetAsync(href);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"The menu links to {href}, which answered {(int)response.StatusCode}.");
        }
    }

    [Fact]
    public async Task Every_picture_and_script_on_the_kiosk_exists()
    {
        var sources = Regex.Matches(await HomePage(), "src=\"(/[^\"?]+)").Select(m => m.Groups[1].Value).Distinct();

        foreach (var src in sources)
        {
            var response = await _client.GetAsync(src);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"The kiosk uses {src}, but there is no such file in wwwroot/.");
        }
    }

    [Fact]
    public async Task Missing_page_shows_the_404_page()
    {
        var response = await _client.GetAsync("/this-page-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("drove off the field", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_check_answers()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Memory_mode_is_announced_on_every_page()
    {
        // Outside development, a site without PGHOST says so, so nobody scans into a void.
        Assert.Contains("Memory mode", await HomePage());
    }

    [Fact]
    public async Task Scans_csv_downloads_a_spreadsheet()
    {
        var response = await _client.GetAsync("/scans.csv?date=2026-10-08");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("id,stamp,name,team,badge", await response.Content.ReadAsStringAsync());
    }
}
