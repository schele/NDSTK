
using System.Threading.RateLimiting;
using Esatto.Umbraco.Backoffice.CookieBanner;
using NDSTK.Booking.Admin;
using NDSTK.Booking.Web;
using Umbraco.Community.BlockPreview.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Server-only secrets (DB connection string, etc). Never committed, so absent locally and in CI.
// Environment variables are re-applied afterwards so they still take precedence over the file.
builder.Configuration
    .AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Per-IP throttling, in two tiers.
//
// Umbraco's member lockout only protects an account that already exists; it does nothing about
// someone hammering the registration form or guessing verification tokens, which is what the Auth
// tier is for. Member actions are a different problem entirely - the caller is already
// authenticated, and booking, cancelling and paying are things a member legitimately does several
// times in a row - so they get their own, far larger budget. One shared tight limit locked ordinary
// members out mid-session.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Partitioned by caller so one abusive client cannot lock out the whole club. Note that
    // everyone behind a single office NAT shares a partition, which is the other reason not to set
    // these tightly.
    static string Caller(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    options.AddPolicy(BookingRateLimits.Auth, context =>
        RateLimitPartition.GetFixedWindowLimiter(Caller(context), _ => new FixedWindowRateLimiterOptions
        {
            // Room for a few mistyped passwords and the page loads around them, while still making
            // a guessing attack pointless.
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
        }));

    options.AddPolicy(BookingRateLimits.MemberActions, context =>
        RateLimitPartition.GetFixedWindowLimiter(Caller(context), _ => new FixedWindowRateLimiterOptions
        {
            // A backstop against a runaway script, not a budget a person can reach by clicking.
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    // Without this the browser shows its own "This page isn't working" for a bare 429, which reads
    // as the site having crashed rather than as being asked to slow down.
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        context.HttpContext.Response.ContentType = "text/html; charset=utf-8";

        await context.HttpContext.Response.WriteAsync(
            """
            <!doctype html><html lang="sv"><head><meta charset="utf-8">
            <title>För många försök</title>
            <link href="/static/css/site.css" rel="stylesheet"></head>
            <body><main class="container"><article class="post">
            <h1>Ta det lugnt en stund</h1>
            <p>Vi har tagit emot många förfrågningar från dig på kort tid. Vänta en minut och
               försök igen.</p>
            <p><a href="/" class="btn-primary">Till startsidan</a></p>
            </article></main></body></html>
            """,
            cancellationToken);
    };
});

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    // Renders each block in the backoffice through the same Razor partial the site uses, so an
    // editor sees the hero, the news list and the widgets rather than a row of labels. Configured
    // here rather than in appsettings.json because these values describe what this site's content
    // model contains - two Umbraco.BlockList data types, no block grid, no rich text blocks - and
    // so should change with the content model, not per environment.
    .AddBlockPreview(options =>
    {
        options.BlockList.Enabled = true;

        // The site's own stylesheet, so a preview is styled by the same rules as the page. Previews
        // render into shadow DOM, which is why site.css declares its custom properties on
        // ":root, :host" - see the comment at the top of that file.
        options.BlockList.Stylesheets = ["/static/css/site.css"];

        // One entry per partial in Views/Partials/blocklist/Components - and that is the whole rule
        // for keeping this list right. Left unset the package previews *every* element type, which
        // is wrong here: cookieDefinition comes from the CookieBanner package and is structured
        // data, not a block with a view - the policy page renders those declarations grouped, never
        // one partial per block - so previewing it put a "view could not be found" panel where the
        // editor used to see a row per cookie. An allowlist also fails the safe way round. Forget
        // to add a block here and it keeps the plain label it has today; the alternative,
        // IgnoredContentTypes, would greet the next data-only element type with that same panel.
        options.BlockList.ContentTypes =
        [
            "heroBlock",
            "newsListBlock",
            "postBlock",
            "textBlock",
            "ctaWidgetBlock",
            "contactWidgetBlock",
            "tagsWidgetBlock",
            "memberWidgetBlock",
        ];

        // ViewLocations is left alone - the package's default for a block list is already
        // /Views/Partials/blocklist/Components/{0}.cshtml, which is where those partials live.

        // Neither editor exists on this site, so nothing would render for them anyway. Stated
        // rather than left at the default, because it is the line that has to change on the day a
        // block grid is added and its blocks show up as labels again.
        options.BlockGrid.Enabled = false;
        options.RichText.Enabled = false;
    })
    .Build();

// The cookie scanner's merge endpoint. Scoped, because it uses IContentService.
builder.Services.AddScoped<NDSTK.CookieScan.CookieScanWriter>();

builder.Services.Configure<NDSTK.CookieScan.CookieScanApiUserOptions>(
    builder.Configuration.GetSection(NDSTK.CookieScan.CookieScanApiUserOptions.SectionName));
builder.Services.AddScoped<NDSTK.CookieScan.CookieScanApiUserSeeder>();

WebApplication app = builder.Build();


await app.BootUmbracoAsync();

// A live endpoint that empties the member tables should say so out loud. This is the one line that
// tells whoever is reading the log why a backoffice user can throw every booking away - and, on a
// site where it was never meant to be on, that it is.
if (app.Services.GetRequiredService<TestDataResetGate>().IsEnabled)
{
    app.Logger.LogWarning(
        "The test data reset is ENABLED. Backoffice users with access to Members can delete every "
        + "booking, payment, credit, child and membership. Development only.");
}

// Creates the cookie scanner's API user when configured to. After BootUmbracoAsync because it
// needs the user service, and awaited rather than fire-and-forget so a failure is logged in order
// rather than interleaved with the first request.
using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider
        .GetRequiredService<NDSTK.CookieScan.CookieScanApiUserSeeder>()
        .SeedAsync(CancellationToken.None);
}

// Maps the endpoint the consent dialog posts decisions to. Must sit after BootUmbracoAsync()
// and before UseUmbraco(); without it the dialog renders but Accept and Reject do nothing.
app.UseCookieConsent();

app.UseHttpsRedirection();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();

        // Must sit here, not before UseUmbraco(). The rate limiting middleware reads the
        // [EnableRateLimiting] policy off the matched endpoint's metadata, so it only works once
        // routing has run - and Umbraco calls UseRouting() from its own
        // RegisterDefaultRequiredMiddleware, which happens before this callback. Registered any
        // earlier there is no endpoint yet, no policy is found, and the limiter silently permits
        // everything: a security control that looks present and does nothing.
        u.AppBuilder.UseRateLimiter();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
