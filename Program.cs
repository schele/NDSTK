
using System.Threading.RateLimiting;
using Esatto.Umbraco.Backoffice.CookieBanner;
using NDSTK.Booking.Web;

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
    .Build();

WebApplication app = builder.Build();


await app.BootUmbracoAsync();

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
