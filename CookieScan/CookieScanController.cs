using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace NDSTK.CookieScan;

/// <summary>
/// The one endpoint the cookie scanner posts its findings to.
/// </summary>
/// <remarks>
/// A narrow, site-owned endpoint rather than the generic document endpoint, because
/// <c>UpdateDocumentRequestModel</c> makes a document PUT a whole-document replace: an omitted
/// property is erased, so a client rebuilding the payload from outside could silently blank the
/// policy page's introduction or outro. Here the merge happens server-side with Umbraco's own
/// Block List types, and the only thing that can be touched is one property of one node.
/// </remarks>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("cookie-scan")]
[ApiExplorerSettings(GroupName = "Cookie scan")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public sealed class CookieScanController(CookieScanWriter writer) : ManagementApiControllerBase
{
    [HttpPost("merge")]
    [ProducesResponseType(typeof(CookieScanMergeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Merge(CookieScanMergeRequest request)
    {
        try
        {
            return Ok(writer.Merge(request));
        }
        catch (CookieScanWriter.RejectedException rejected)
        {
            // Everything the caller could fix comes back as a 400 with the reason in plain text,
            // because the caller is a command-line tool printing it straight to an operator.
            return BadRequest(new { message = rejected.Message });
        }
    }
}
