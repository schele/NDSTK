using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace NDSTK.Booking.Admin;

/// <summary>
/// The management API behind the Medlemmar dashboard and the class roster.
/// </summary>
/// <remarks>
/// Gated on SectionAccessMembers, so authorisation is Umbraco's own rather than a check of ours.
///
/// Every read here is a report. The only writes are the test data resets at the bottom, which exist
/// for walking the booking flow from a clean slate and answer 404 unless
/// <see cref="TestDataResetGate"/> lets them. An administrator correcting a member's birth date is
/// still a separate request - one that would need its own audit trail, which a dashboard does not
/// have.
/// </remarks>
[ApiController]
[VersionedApiBackOfficeRoute("backoffice/ndstk/members")]
[ApiExplorerSettings(GroupName = "NDSTK Member Administration")]
[Authorize(Policy = AuthorizationPolicies.SectionAccessMembers)]
public sealed class MemberAdminController(
    MemberAdminQueries queries,
    TestDataReset reset,
    TestDataResetGate gate) : ManagementApiControllerBase
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

    // ------------------------------------------------------------- test data reset

    /// <summary>
    /// Whether the reset is available here. The dashboard asks before drawing its buttons, so a
    /// site where this is switched off shows no control rather than one that fails when pressed.
    /// </summary>
    [HttpGet("reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetResetAvailability()
        => gate.IsEnabled ? Ok(new { available = true }) : NotFound();

    /// <summary>Clears every account's bookings, payments, credits, children and membership.</summary>
    [HttpPost("reset")]
    [ProducesResponseType(typeof(TestDataResetResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetEverything()
        => gate.IsEnabled ? Ok(await reset.ResetEverythingAsync()) : NotFound();

    /// <summary>Clears one account, leaving the other accounts and their places alone.</summary>
    [HttpPost("reset/{memberKey:guid}")]
    [ProducesResponseType(typeof(TestDataResetResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetMember(Guid memberKey)
        => gate.IsEnabled ? Ok(await reset.ResetMemberAsync(memberKey)) : NotFound();
}
