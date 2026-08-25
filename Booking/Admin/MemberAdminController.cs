using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace NDSTK.Booking.Admin;

/// <summary>
/// Read-only management API behind the Medlemmar dashboard and the class roster.
/// </summary>
/// <remarks>
/// Gated on SectionAccessMembers, so authorisation is Umbraco's own rather than a check of ours.
///
/// There are deliberately no write endpoints. Members manage their own children from the portal,
/// and an administrator correcting a birth date is a separate request - one that would need its own
/// audit trail, which a dashboard does not have.
/// </remarks>
[ApiController]
[VersionedApiBackOfficeRoute("backoffice/ndstk/members")]
[ApiExplorerSettings(GroupName = "NDSTK Member Administration")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessMembers)]
public sealed class MemberAdminController(MemberAdminQueries queries) : ManagementApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MemberAdminRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll() => Ok(await queries.GetMembersAsync());

    [HttpGet("{memberKey:guid}")]
    [ProducesResponseType(typeof(MemberAdminDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOne(Guid memberKey)
        => await queries.GetDetailAsync(memberKey) is { } detail ? Ok(detail) : NotFound();

    [HttpGet("roster/{classKey:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<ClassRosterRow>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoster(Guid classKey)
        => Ok(await queries.GetRosterAsync(classKey));
}
