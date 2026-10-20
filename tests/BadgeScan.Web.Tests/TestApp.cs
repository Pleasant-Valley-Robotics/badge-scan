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

    public FakeTimeProvider Clock { get; } = new(Start);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("BADGESCAN_STORE", "memory");
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock));
    }
}
