// Program.cs is where the website starts. You don't need to change it to change
// the words on a page or to add a page: that is all done in the Pages/ folder.

using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Razor Pages: every .cshtml file in Pages/ is a page of the site.
builder.Services.AddRazorPages();

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

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    // A crash shows a friendly page (Pages/Error.cshtml) instead of the error details.
    app.UseExceptionHandler("/error");
}

// A page that doesn't exist shows our 404 page (Pages/NotFound.cshtml).
app.UseStatusCodePagesWithReExecute("/not-found");

// Files in wwwroot/ (CSS, images, fonts) are sent as they are.
app.UseStaticFiles();

app.UseRouting();

app.MapHealthChecks("/healthz");

// Pages/Index.cshtml is "/", Pages/Robot.cshtml is "/robot", and so on.
app.MapRazorPages();

app.Run();

// Lets the tests in tests/TeamSite.Web.Tests start the site in memory.
public partial class Program { }
