
using System.Threading.RateLimiting;
using Esatto.Umbraco.Backoffice.CookieBanner;
using NDSTK.Booking.Web;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Server-only secrets (DB connection string, etc). Never committed, so absent locally and in CI.
// Environment variables are re-applied afterwards so they still take precedence over the file.
builder.Configuration
    .AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Per-IP throttle on the member forms. Registration, login and verification are the endpoints
// worth brute-forcing, and Umbraco's member lockout only protects an account that already exists -
// it does nothing about someone hammering the registration form or guessing verification tokens.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(BookingRateLimits.MemberForms, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Partitioned by caller so one abusive client cannot lock out the whole club.
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));
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
