
using Esatto.Umbraco.Backoffice.CookieBanner;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Server-only secrets (DB connection string, etc). Never committed, so absent locally and in CI.
// Environment variables are re-applied afterwards so they still take precedence over the file.
builder.Configuration
    .AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

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
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
