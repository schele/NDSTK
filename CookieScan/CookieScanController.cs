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
// No [ApiExplorerSettings(GroupName = ...)] here (there was one, "Cookie scan"): decompiling
// Umbraco.Cms.Api.Common.OpenApi.BackOfficeOpenApiDocumentBuilder.Build shows the "management"
// document's ShouldInclude checks only for a MapToApiAttribute matching "management" - which this
// controller already inherits from ManagementApiControllerBase, the same way every other
// management controller does - and never looks at ApiExplorerSettings.GroupName at all (that only
// drives tag grouping within a document, via TagActionsByGroupNameTransformer). So GroupName was
// not, in fact, the reason this endpoint was missing from every swagger document; the actual cause
// could not be confirmed from static analysis alone. Removed rather than guess a replacement value,
// since the default (no explicit group) is more likely correct than an invented one. Re-verify
// against a running site.
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
