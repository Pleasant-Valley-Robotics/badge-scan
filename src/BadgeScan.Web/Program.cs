// Program.cs is where the site starts. It sets up what the site uses, then lists, in
// order, what happens to every request that comes in.

using BadgeScan.Web.Data;
using BadgeScan.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Razor Pages for the screens people look at (Pages/); controllers for the JSON that the
// kiosk's JavaScript and other programs talk to (Controllers/Api/).
builder.Services.AddRazorPages();
builder.Services.AddControllers();

// /healthz answers "Healthy" when the site is running. The robotics server asks it
// before it switches visitors to a new release.
builder.Services.AddHealthChecks();

// On the robotics server, visitors reach us through a proxy that tells us they used
// https with the X-Forwarded-Proto header. Trust it so links we build use https.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// The time comes from one place, so the tests can set the clock.
builder.Services.AddSingleton(TimeProvider.System);
// Every saved scan is handed to every open kiosk (the live list on /api/scans/stream).
builder.Services.AddSingleton<ScanBroadcaster>();
builder.Services.AddSingleton<ScanRecorder>();
builder.Services.AddSingleton<Importer>();

// Where badges and scans are kept. The robotics server hands us PGHOST and friends through
// compose.yaml. Without them (GitHub's check, a laptop without PostgreSQL), everything is
// kept in memory and is gone when the site stops. BADGESCAN_STORE=memory forces that.
var connectionString = DatabaseOptions.ConnectionStringFromEnvironment(builder.Configuration);
var inMemory = builder.Configuration["BADGESCAN_STORE"] == "memory" || connectionString is null;
builder.Services.AddSingleton(new StoreInfo(inMemory));
if (inMemory)
{
    builder.Services.AddSingleton<IBadgeScanStore, InMemoryStore>();
}
else
{
    builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString!));
    builder.Services.AddSingleton<IBadgeScanStore, PostgresStore>();
}

var app = builder.Build();

// Central time needs the time zone data in the container. Better to find out now than at
// the first scan.
_ = CentralTime.Zone;

if (inMemory)
{
    app.Logger.LogWarning("PGHOST is not set: badges and scans are kept in memory only and will be lost when the site stops");
}
else
{
    // Bring the database up to date before anyone can use it.
    await Migrator.RunWithRetryAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), app.Logger, app.Lifetime.ApplicationStopping);
}

app.UseForwardedHeaders();

var isApi = (HttpContext context) => context.Request.Path.StartsWithSegments("/api");

if (!app.Environment.IsDevelopment())
{
    // A crash shows a friendly page (Pages/Error.cshtml) instead of the error details;
    // the JSON side gets a short JSON answer instead.
    app.UseWhen(c => !isApi(c), pages => pages.UseExceptionHandler("/error"));
    app.UseWhen(isApi, api => api.UseExceptionHandler(handler => handler.Run(async context =>
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { ok = false, error = "Something went wrong" });
    })));
}

// A page that doesn't exist shows our 404 page (Pages/NotFound.cshtml). JSON answers
// such as 404 "badge not recognised" are left as they are.
app.UseWhen(c => !isApi(c), pages => pages.UseStatusCodePagesWithReExecute("/not-found"));

// Files in wwwroot/ (CSS, JavaScript, images, fonts) are sent as they are.
app.UseStaticFiles();

app.UseRouting();

app.MapHealthChecks("/healthz");

// Pages/Index.cshtml is "/", Pages/Badges.cshtml is "/badges", and so on.
app.MapRazorPages();
// Controllers/Api/*: /api/scans, /api/badges, /scans.csv.
app.MapControllers();

await app.RunAsync();

// Lets the tests in tests/BadgeScan.Web.Tests start the site in memory.
public partial class Program { }
