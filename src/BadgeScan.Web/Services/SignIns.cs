using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using BadgeScan.Web.Data;
using BadgeScan.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BadgeScan.Web.Services;

/// <summary>
/// Who is on the other end of a request. There are three kinds:
///  - staff:   a lead, coach or mentor signed in with their own account. May do everything.
///  - kiosk:   a browser someone turned into the kiosk by starting a meeting. May scan badges
///             and see today's last few scans, nothing else, until the meeting ends.
///  - program: another program (Project-Tracker) with a data key. May read scans, nothing else.
/// Staff and kiosk sign-ins are a cookie; a program sends "Authorization: Bearer bsk_...".
/// </summary>
public static class SignIns
{
    public const string Kind = "kind";
    public const string Staff = "staff";
    public const string Kiosk = "kiosk";
    public const string Program = "program";
    private const string MeetingClaim = "meeting";
    private const string StampClaim = "stamp";

    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string KeyScheme = "DataKey";

    /// <summary>The policies the pages and controllers ask for. Anything that doesn't say is StaffOnly.</summary>
    public static class Policies
    {
        public const string StaffOnly = "staff";
        public const string Scan = "scan";            // staff or kiosk
        public const string ReadScans = "read";       // staff or program
        public const string RecentScans = "recent";   // staff, kiosk or program
    }

    private static readonly object AccountItem = new();
    private static readonly object MeetingItem = new();

    public static ClaimsPrincipal ForAccount(Account account) => new(new ClaimsIdentity(
    [
        new Claim(Kind, Staff),
        new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
        new Claim(ClaimTypes.Name, account.DisplayName),
        new Claim(StampClaim, account.SecurityStamp),
    ], CookieScheme));

    public static ClaimsPrincipal ForMeeting(Meeting meeting) => new(new ClaimsIdentity(
    [
        new Claim(Kind, Kiosk),
        new Claim(MeetingClaim, meeting.Id.ToString()),
        new Claim(ClaimTypes.Name, $"Kiosk opened by {meeting.OpenedByName}"),
    ], CookieScheme));

    public static bool IsKiosk(this ClaimsPrincipal user) => user.HasClaim(Kind, Kiosk);

    /// <summary>The signed-in lead, coach or mentor, checked against the database on this request.</summary>
    public static Account? CurrentAccount(this HttpContext context) => context.Items[AccountItem] as Account;

    /// <summary>The meeting this kiosk is running, checked against the database on this request.</summary>
    public static Meeting? CurrentMeeting(this HttpContext context) => context.Items[MeetingItem] as Meeting;

    /// <summary>
    /// The cookie lasts 12 hours from its last use, longer than any meeting, so a kiosk laptop
    /// that restarts keeps its meeting. When a kiosk's meeting ends is checked on every request.
    /// </summary>
    public static Task SignInAsync(HttpContext context, ClaimsPrincipal principal)
        => context.SignInAsync(CookieScheme, principal, new AuthenticationProperties { IsPersistent = true });

    public static Task SignOutAsync(HttpContext context) => context.SignOutAsync(CookieScheme);

    private static bool IsApi(HttpContext context) => context.Request.Path.StartsWithSegments("/api");

    public static void AddSignIns(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(CookieScheme)
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = "pv_attendance";
                options.Cookie.HttpOnly = true;
                // Lax: sent when someone follows a link here (from Project-Tracker, say), never
                // with a form or script on another site posting here.
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.SlidingExpiration = true;
                options.LoginPath = "/login";
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = Validate,
                    OnRedirectToLogin = context =>
                    {
                        if (IsApi(context.HttpContext))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return context.Response.WriteAsJsonAsync(new { ok = false, error = "Signed out. Reload the page and sign in." });
                        }
                        context.Response.Redirect(context.RedirectUri);
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        if (IsApi(context.HttpContext))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return context.Response.WriteAsJsonAsync(new { ok = false, error = "Not allowed from here" });
                        }
                        // The only thing a signed-in browser can't open is a staff page from the kiosk.
                        context.Response.Redirect(context.HttpContext.User.IsKiosk() ? "/kiosk" : "/");
                        return Task.CompletedTask;
                    },
                };
            })
            .AddScheme<AuthenticationSchemeOptions, DataKeyHandler>(KeyScheme, null);

        var staffOnly = new AuthorizationPolicyBuilder(CookieScheme).RequireClaim(Kind, Staff).Build();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(staffOnly)
            .AddPolicy(Policies.StaffOnly, staffOnly)
            .AddPolicy(Policies.Scan, p => p.AddAuthenticationSchemes(CookieScheme).RequireClaim(Kind, Staff, Kiosk))
            .AddPolicy(Policies.ReadScans, p => p.AddAuthenticationSchemes(CookieScheme, KeyScheme).RequireClaim(Kind, Staff, Program))
            .AddPolicy(Policies.RecentScans, p => p.AddAuthenticationSchemes(CookieScheme, KeyScheme).RequireClaim(Kind, Staff, Kiosk, Program));

        // Guessing passwords: each account locks after a few wrong ones (AccountService), and
        // each visitor gets this many sign-in attempts a minute across all accounts.
        var perMinute = int.TryParse(configuration["BADGESCAN_SIGNIN_PER_MINUTE"], out var n) ? n : 10;
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, ct) => new ValueTask(context.HttpContext.Response.WriteAsync(
                "Too many sign-in attempts from here. Wait a minute and try again.", ct));
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                context.Request.Method == "POST" && (context.Request.Path == "/login" || context.Request.Path == "/kiosk/end")
                    ? RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context),
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) })
                    : RateLimitPartition.GetNoLimiter(""));
        });
    }

    /// <summary>
    /// Every request: is this sign-in still good? A staff sign-in ends when the account is
    /// turned off or its password changes; a kiosk's when its meeting is ended or runs out.
    /// </summary>
    private static async Task Validate(CookieValidatePrincipalContext context)
    {
        var http = context.HttpContext;
        var store = http.RequestServices.GetRequiredService<IAccountStore>();
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();
        var user = context.Principal!;

        if (user.HasClaim(Kind, Staff) && long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
        {
            var account = await store.GetAccountAsync(accountId, http.RequestAborted);
            if (account is { Disabled: false } && account.SecurityStamp == user.FindFirstValue(StampClaim))
            {
                http.Items[AccountItem] = account;
                return;
            }
        }
        else if (user.IsKiosk() && long.TryParse(user.FindFirstValue(MeetingClaim), out var meetingId))
        {
            var meeting = await store.GetMeetingAsync(meetingId, http.RequestAborted);
            if (meeting is not null && meeting.IsOpenAt(clock.GetUtcNow()))
            {
                http.Items[MeetingItem] = meeting;
                return;
            }
        }

        context.RejectPrincipal();
        await SignOutAsync(http);
    }

    /// <summary>
    /// Someone signed in with a temporary password goes nowhere else until they choose their
    /// own. Goes after UseAuthentication.
    /// </summary>
    public static IApplicationBuilder UsePasswordChangeFirst(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        if (context.CurrentAccount() is { MustChangePassword: true } && path != "/account/password" && path != "/logout" && path != "/healthz")
        {
            if (IsApi(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { ok = false, error = "Choose your own password first" });
            }
            else context.Response.Redirect("/account/password");
            return;
        }
        await next();
    });

    /// <summary>
    /// The visitor's address. Visitors arrive only through Cloudflare, which puts it in
    /// CF-Connecting-IP (and replaces any the visitor sent); the site's port is open to the
    /// machine itself only.
    /// </summary>
    public static string ClientAddress(HttpContext context)
        => context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
           ?? context.Connection.RemoteIpAddress?.ToString()
           ?? "unknown";

    /// <summary>
    /// "Authorization: Bearer bsk_..." from another program. Says nothing at all when there's
    /// no such header, so a browser that isn't signed in is still sent to the sign-in page.
    /// </summary>
    public sealed class DataKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AccountService accounts)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        private string? Bearer()
        {
            var header = Request.Headers.Authorization.ToString();
            return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var secret = Bearer();
            if (secret is null) return AuthenticateResult.NoResult();
            var key = await accounts.FindApiKeyAsync(secret, Context.RequestAborted);
            if (key is null) return AuthenticateResult.Fail("Unknown or revoked data key");
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(Kind, Program),
                new Claim(ClaimTypes.NameIdentifier, key.Id.ToString()),
                new Claim(ClaimTypes.Name, key.Label),
            ], Scheme.Name));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }

        // The cookie side may already have answered (401 or 403, with a message); then there's nothing to add.
        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
            => Bearer() is null || Response.HasStarted ? Task.CompletedTask : base.HandleChallengeAsync(properties);

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
            => Bearer() is null || Response.HasStarted ? Task.CompletedTask : base.HandleForbiddenAsync(properties);
    }
}
