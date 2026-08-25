using Microsoft.Extensions.Logging;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace NDSTK.Booking.Services;

/// <summary>
/// Reads and writes the two membership facts stored as member type properties: when the paid
/// membership runs out, and whether the account is a family account.
/// </summary>
/// <remarks>
/// They live on the member rather than in the booking tables so that an administrator can comp a
/// membership, or grant a family account, from the backoffice without touching SQL.
///
/// The welcome price used to live here too. It moved onto the participant when children arrived:
/// it is once per child, and a per-account flag would hand a second child their sibling's spent
/// discount. See <see cref="Data.IParticipantRepository.TryStampFirstClassUsedAsync"/>.
/// </remarks>
public sealed class MemberProfileService(
    IMemberService memberService,
    ILogger<MemberProfileService> logger)
{
    internal const string MembershipPaidUntilAlias = "membershipPaidUntil";
    internal const string FamilyAccountAlias = "familjekonto";
    internal const string PhoneAlias = "telefon";

    /// <summary>
    /// The account's pricing-relevant state. A member who cannot be found is treated as brand new
    /// rather than throwing: the caller is about to quote a price, and quoting the full joining
    /// price for an unknown member fails safe.
    /// </summary>
    public async Task<MemberState> GetStateAsync(Guid memberKey)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member is null)
        {
            logger.LogWarning("Member {MemberKey} was not found; treating them as new.", memberKey);
            return new MemberState(null, IsFamilyAccount: false);
        }

        return new MemberState(ReadPaidUntil(member), member.GetValue<bool>(FamilyAccountAlias));
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

        DateOnly paidUntil = today.AddDays(Pricing.MembershipDays);
        member.SetValue(MembershipPaidUntilAlias, paidUntil.ToDateTime(TimeOnly.MinValue));
        memberService.Save(member);

        logger.LogInformation(
            "Membership for {MemberKey} now runs to {PaidUntil}.", memberKey, paidUntil);
    }

    /// <summary>
    /// Turns an account into a family account, so it may hold more than one child.
    /// </summary>
    /// <remarks>
    /// Deliberately does not touch the expiry date. See <see cref="Pricing.FamilyUpgradeQuote"/>:
    /// if paying the supplement moved the date forward a year it would be a cheaper renewal than
    /// the annual fee, and nobody would ever pay the annual fee twice.
    /// </remarks>
    public async Task SetFamilyAccountAsync(Guid memberKey)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member is null)
        {
            logger.LogError("Cannot upgrade {MemberKey} to a family account: not found.", memberKey);
            return;
        }

        member.SetValue(FamilyAccountAlias, true);
        memberService.Save(member);

        logger.LogInformation("Member {MemberKey} is now a family account.", memberKey);
    }

    /// <summary>
    /// Drops an account back to a solo account, when it no longer has more than one child.
    /// </summary>
    /// <remarks>
    /// No refund, in keeping with the rest of the model - what it buys is a cheaper renewal next
    /// time, not money back now. The supplement they already paid for the current year is not lost
    /// either: re-activating inside that year is free, which
    /// <see cref="Data.IBookingRepository.HasPaidFamilyFeeSinceAsync"/> is there to establish.
    /// </remarks>
    public async Task ClearFamilyAccountAsync(Guid memberKey)
    {
        IMember? member = (await memberService.GetByKeysAsync(memberKey)).FirstOrDefault();
        if (member is null)
        {
            logger.LogError("Cannot downgrade {MemberKey} to a solo account: not found.", memberKey);
            return;
        }

        member.SetValue(FamilyAccountAlias, false);
        memberService.Save(member);

        logger.LogInformation("Member {MemberKey} is back to a solo account.", memberKey);
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
