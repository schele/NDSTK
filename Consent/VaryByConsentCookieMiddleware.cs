using Microsoft.AspNetCore.Http;

namespace NDSTK.Consent;

/// <summary>
/// Marks front-end HTML responses as private and varying by the consent cookie.
/// </summary>
/// <remarks>
/// The consent bar, and any consent-gated <c>&lt;script&gt;</c> tags such as the Google tag, are
/// baked into server-rendered markup based on the visitor's consent cookie. Nothing caches that
/// markup today, but the site deploys behind Railway's edge, so the moment any shared cache does
/// handle it, one visitor's consent state - including a third-party analytics tag - could be served
/// to another. Scoped to <c>text/html</c> responses outside <c>/umbraco</c>: static assets and API
/// responses never carry <c>text/html</c>, and the backoffice is explicitly excluded by path, so
/// neither is affected.
/// </remarks>
internal sealed class VaryByConsentCookieMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/umbraco") is false)
        {
            context.Response.OnStarting(() =>
            {
                if (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) is true)
                {
                    context.Response.Headers.Vary = "Cookie";
                    context.Response.Headers.CacheControl = "private, no-cache";
                }

                return Task.CompletedTask;
            });
        }

        await next(context);
    }
}
