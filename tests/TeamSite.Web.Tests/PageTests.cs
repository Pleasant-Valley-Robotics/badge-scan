using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TeamSite.Web.Tests;

/// <summary>
/// These tests start the whole website in memory and visit its pages, the way a
/// browser would. GitHub runs them on every pull request; if one fails, the pull
/// request can't be merged until it's fixed.
/// Run them yourself with:  dotnet test
/// </summary>
public class PageTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    // The home page as a visitor gets it, without the <!-- notes --> (they aren't shown).
    private async Task<string> HomePage()
    {
        var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Regex.Replace(await response.Content.ReadAsStringAsync(), "<!--.*?-->", "", RegexOptions.Singleline);
    }

    [Fact]
    public async Task Home_page_shows_with_its_menu()
    {
        var html = await HomePage();

        Assert.Contains("<h1>", html);
        Assert.Contains("<nav", html);
    }

    [Fact]
    public async Task Every_menu_link_works()
    {
        // Reads the menu from the home page, so a link added to _Layout.cshtml is checked too.
        var nav = Regex.Match(await HomePage(), "<nav.*?</nav>", RegexOptions.Singleline).Value;
        var links = Regex.Matches(nav, "href=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(links);

        // Links to other websites (https://...) aren't ours to check.
        foreach (var href in links.Where(h => h.StartsWith('/')))
        {
            var response = await _client.GetAsync(href);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"The menu links to {href}, which answered {(int)response.StatusCode}. Is there a page for it in Pages/?");
        }
    }

    [Fact]
    public async Task Every_picture_on_the_home_page_exists()
    {
        var images = Regex.Matches(await HomePage(), "src=\"(/[^\"]+)\"").Select(m => m.Groups[1].Value).Distinct();

        foreach (var src in images)
        {
            var response = await _client.GetAsync(src);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"The home page shows {src}, but there is no such file in wwwroot/.");
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
        // The robotics server asks this before a release counts.
        var response = await _client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
