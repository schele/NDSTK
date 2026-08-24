using Microsoft.Extensions.Logging;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace NDSTK.Booking.Services;

/// <summary>
/// Reads and writes the two membership facts stored as member type properties: when the paid
/// membership runs out, and whether the once-per-account welcome price has been used.
/// </summary>
/// <remarks>
/// They live on the member rather than in the booking tables so that an administrator can comp a
/// membership, or reset someone's welcome price, from the backoffice without touching SQL.
/// </remarks>
public sealed class MemberProfileService(
    IMemberService memberService,
    ILogger<MemberProfileService> logger)
{
    internal const string MembershipPaidUntilAlias = "membershipPaidUntil";
    internal const string FirstClassDiscountUsedAlias = "firstClassDiscountUsed";

    /// <summary>
    /// The member's pricing-relevant state. A member who cannot be found is treated as brand new
    /// rather than throwing: the caller is about to quote a price, and quoting the full
    /// joining price for an unknown member fails safe.
    /// </summary>
    public async Task<MemberState> GetStateAsync(Guid memberKey)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member is null)
        {
            logger.LogWarning("Member {MemberKey} was not found; treating them as new.", memberKey);
            return new MemberState(null, FirstClassDiscountUsed: false);
        }

        return new MemberState(
            ReadPaidUntil(member),
            member.GetValue<bool>(FirstClassDiscountUsedAlias));
    }

    /// <summary>
    /// Extends the membership by a year from today. Called only once a payment that included the
    /// membership fee has completed.
    /// </summary>
    public async Task ExtendMembershipAsync(Guid memberKey, DateOnly today)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member is null)
        {
            logger.LogError("Cannot extend the membership of {MemberKey}: not found.", memberKey);
            return;
        }

        DateOnly paidUntil = today.AddDays(365);
        member.SetValue(MembershipPaidUntilAlias, paidUntil.ToDateTime(TimeOnly.MinValue));
        memberService.Save(member);

        logger.LogInformation(
            "Membership for {MemberKey} now runs to {PaidUntil}.", memberKey, paidUntil);
    }

    /// <summary>Marks the welcome price as spent. Called only when a payment that charged it completes.</summary>
    public async Task MarkFirstClassDiscountUsedAsync(Guid memberKey)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member is null)
        {
            logger.LogError("Cannot mark the first-class discount used for {MemberKey}: not found.", memberKey);
            return;
        }

        member.SetValue(FirstClassDiscountUsedAlias, true);
        memberService.Save(member);
    }

    /// <summary>
    /// The date picker stores a DateTime. Only the date part carries meaning - the membership is
    /// valid through the whole of its last day - so the time is dropped rather than compared.
    /// </summary>
    private static DateOnly? ReadPaidUntil(IMember member)
    {
        var stored = member.GetValue<DateTime?>(MembershipPaidUntilAlias);
        return stored is null ? null : DateOnly.FromDateTime(stored.Value);
    }
}
