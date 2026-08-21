using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

public class ConsentControllerTests
{
    private static (ConsentController Controller, DefaultHttpContext Context) Build(int policyVersion = 1)
    {
        var options = Options.Create(new ConsentOptions { PolicyVersion = policyVersion });
        var context = new DefaultHttpContext();
        var controller = new ConsentController(new ConsentCookieWriter(options))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        return (controller, context);
    }

    // Brief's exact assertion (task-3-brief.md Step 1), transcribed verbatim; the analyzer would
    // rather see the filtering overload of Assert.Single, but that changes the transcribed code,
    // so the warning is suppressed here instead to keep the build at 0 warnings.
#pragma warning disable xUnit2031
    private static string SetCookieHeader(DefaultHttpContext context)
        => Assert.Single(context.Response.Headers.SetCookie.ToArray().Where(h => h is not null))!;
#pragma warning restore xUnit2031

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
}
