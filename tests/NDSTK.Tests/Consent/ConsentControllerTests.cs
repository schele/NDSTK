using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

public class ConsentControllerTests
{
    private static (ConsentController Controller, DefaultHttpContext Context) Build(
        int policyVersion = 1,
        int cookieLifetimeDays = 365)
    {
        var options = Options.Create(new ConsentOptions
        {
            PolicyVersion = policyVersion,
            CookieLifetimeDays = cookieLifetimeDays,
        });
        var context = new DefaultHttpContext();
        var controller = new ConsentController(new ConsentCookieWriter(options))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        return (controller, context);
    }

    private static string SetCookieHeader(DefaultHttpContext context)
        => Assert.Single(context.Response.Headers.SetCookie.ToArray(), h => h is not null)!;

    [Fact]
    public void Accepting_sets_the_cookie_and_returns_the_state()
    {
        (ConsentController controller, DefaultHttpContext context) = Build();

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = ["statistics", "marketing"],
            Action = "accept-all",
            Culture = "sv",
        });

        ConsentStateResponse response = Assert.IsType<ConsentStateResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(1, response.Version);
        Assert.Equal(["marketing", "statistics"], response.Categories);
        Assert.NotEmpty(response.ConsentId);

        var header = SetCookieHeader(context);
        Assert.Contains("ndstk-consent=", header);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejecting_stores_no_categories()
    {
        (ConsentController controller, _) = Build();

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = [],
            Action = "reject-all",
            Culture = "sv",
        });

        ConsentStateResponse response = Assert.IsType<ConsentStateResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Empty(response.Categories);
    }

    [Fact]
    public void Unknown_categories_are_discarded_rather_than_trusted()
    {
        (ConsentController controller, _) = Build();

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = ["statistics", "telepathy", "necessary"],
            Action = "custom",
            Culture = "sv",
        });

        ConsentStateResponse response = Assert.IsType<ConsentStateResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(["statistics"], response.Categories);
    }

    [Fact]
    public void An_unknown_action_is_rejected()
    {
        (ConsentController controller, _) = Build();

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = [],
            Action = "definitely-not-an-action",
            Culture = "sv",
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void The_cookie_records_the_current_policy_version()
    {
        (ConsentController controller, _) = Build(policyVersion: 7);

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = [],
            Action = "reject-all",
            Culture = "sv",
        });

        ConsentStateResponse response = Assert.IsType<ConsentStateResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(7, response.Version);
    }

    [Fact]
    public void The_cookie_value_is_encoded_exactly_once()
    {
        // Pins the actual wire format. Response.Cookies.Append is what URL-encodes the cookie
        // value on its way into the Set-Cookie header; if ConsentCookieCodec.Encode escapes it
        // too, this single decode still leaves an escaped string and JsonDocument.Parse throws
        // instead of finding "v" — exactly the class of bug a mere "contains ndstk-consent=" check
        // cannot catch, and the one Task 10's browser-side single decodeURIComponent would hit.
        (ConsentController controller, DefaultHttpContext context) = Build();

        controller.Post(new ConsentRequest
        {
            Categories = [],
            Action = "reject-all",
            Culture = "sv",
        });

        var header = SetCookieHeader(context);
        var rawValue = SetCookieHeaderValue.Parse(header).Value.ToString();
        var decodedOnce = Uri.UnescapeDataString(rawValue);

        using JsonDocument json = JsonDocument.Parse(decodedOnce);
        Assert.Equal(1, json.RootElement.GetProperty("v").GetInt32());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Secure_attribute_tracks_the_request_scheme(bool isHttps)
    {
        (ConsentController controller, DefaultHttpContext context) = Build();
        context.Request.IsHttps = isHttps;

        controller.Post(new ConsentRequest
        {
            Categories = [],
            Action = "reject-all",
            Culture = "sv",
        });

        var header = SetCookieHeader(context);

        if (isHttps)
        {
            Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.DoesNotContain("secure", header, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Expiry_tracks_the_configured_cookie_lifetime_rather_than_the_365_day_default()
    {
        (ConsentController controller, DefaultHttpContext context) = Build(cookieLifetimeDays: 30);

        controller.Post(new ConsentRequest
        {
            Categories = [],
            Action = "reject-all",
            Culture = "sv",
        });

        var header = SetCookieHeader(context);
        DateTimeOffset? expires = SetCookieHeaderValue.Parse(header).Expires;

        Assert.NotNull(expires);

        // Day count, not an exact timestamp, so test-runner latency cannot make this flaky. 30
        // falls nowhere near the 365-day default, so a writer that ignored CookieLifetimeDays
        // still fails this even with a generous window.
        var daysUntilExpiry = (expires!.Value - DateTimeOffset.UtcNow).TotalDays;
        Assert.InRange(daysUntilExpiry, 29, 31);
    }
}
