
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Server-only secrets (DB connection string, etc). Never committed, so absent locally and in CI.
// Environment variables are re-applied afterwards so they still take precedence over the file.
builder.Configuration
    .AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    rateLimiter.AddPolicy(NDSTK.Consent.ConsentRateLimiting.PolicyName, httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
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

app.UseHttpsRedirection();

// UseUmbraco().WithMiddleware(...) runs UseRouting() (plus Umbraco's own auth/session/website
// middleware) internally before returning, and WithEndpoints(...) below is what finally calls
// UseEndpoints(). UseRateLimiter() has to sit after routing has resolved an endpoint (it reads
// [EnableRateLimiting] from the matched endpoint's metadata) and before that endpoint actually
// runs, so the chain is split here instead of composed as one fluent expression.
var umbracoEndpointBuilder = app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    });

app.UseRateLimiter();

umbracoEndpointBuilder.WithEndpoints(u =>
{
    u.UseBackOfficeEndpoints();
    u.UseWebsiteEndpoints();
});

app.MapControllers();

await app.RunAsync();
