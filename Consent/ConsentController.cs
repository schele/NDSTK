using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace NDSTK.Consent;

[ApiController]
[Route("api/consent")]
public sealed class ConsentController(ConsentCookieWriter cookieWriter) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(ConsentRateLimiting.PolicyName)]
    public ActionResult<ConsentStateResponse> Post([FromBody] ConsentRequest request)
    {
        if (ConsentCookieWriter.TryParseAction(request.Action, out _) is false)
        {
            return BadRequest(new { error = "Unknown consent action." });
        }

        ConsentDecision decision = cookieWriter.Write(Response, request);

        return Ok(new ConsentStateResponse(
            decision.PolicyVersion,
            decision.Granted.Select(ConsentCategories.ToWireName).Order(StringComparer.Ordinal).ToArray(),
            decision.ConsentId,
            decision.DecidedAt.ToString("O")));
    }
}
