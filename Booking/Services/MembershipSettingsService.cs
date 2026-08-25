using NDSTK.Booking.Domain;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;

namespace NDSTK.Booking.Services;

/// <summary>
/// Reads the booking configuration off the Settings node, the same node the layout already reads
/// the menu and sidebar from.
/// </summary>
/// <remarks>
/// Every value falls back to <see cref="MembershipSettings.Defaults"/> individually rather than as
/// a block, so an editor who fills in only the class price still gets sensible behaviour for the
/// rest. A zero or negative value counts as "not set": a free class or a zero-minute payment hold
/// is far more likely to be an empty field than a deliberate choice, and treating zero as
/// deliberate would silently give classes away.
/// </remarks>
public sealed class MembershipSettingsService(IPublishedContentQuery contentQuery)
{
    public MembershipSettings Get()
    {
        IPublishedContent? settings = FindSettingsNode();
        if (settings is null)
        {
            return MembershipSettings.Defaults;
        }

        PriceList defaults = MembershipSettings.Defaults.Prices;

        return new MembershipSettings(
            new PriceList(
                MembershipFeeOre: KronorToOre(settings, "membershipFee", defaults.MembershipFeeOre),
                FamilyFeeOre: KronorToOre(settings, "familyFee", defaults.FamilyFeeOre),
                FirstClassPriceOre: KronorToOre(settings, "firstClassPrice", defaults.FirstClassPriceOre),
                ClassPriceOre: KronorToOre(settings, "classPrice", defaults.ClassPriceOre)),
            ReminderHoursBefore: PositiveOrDefault(
                settings, "reminderHoursBefore", MembershipSettings.Defaults.ReminderHoursBefore),
            PaymentHoldMinutes: PositiveOrDefault(
                settings, "paymentHoldMinutes", MembershipSettings.Defaults.PaymentHoldMinutes),
            // Zero counts as "not set" here like everywhere else on this node. Umbraco's numeric
            // editor cannot tell an emptied field from a deliberate 0 - both read back as 0 - so
            // there is no way to express "no deadline" through the field, and falling back to
            // twelve is the safer of the two readings for a club that does not want late
            // cancellations at all.
            CancellationDeadlineHours: PositiveOrDefault(
                settings, "cancellationDeadlineHours",
                MembershipSettings.Defaults.CancellationDeadlineHours));
    }

    /// <summary>The page a member lands on after signing in.</summary>
    public IPublishedContent? GetMemberPortalPage()
        => FindSettingsNode()?.Value<IPublishedContent>("memberPortalPage");

    /// <summary>The target of the "Bli medlem" buttons.</summary>
    public IPublishedContent? GetRegisterPage()
        => FindSettingsNode()?.Value<IPublishedContent>("registerPage");

    /// <summary>
    /// Same lookup the layout in Root.cshtml already does: the settings node is a child of the
    /// site root. Done through IPublishedContentQuery rather than UmbracoHelper so this works
    /// outside a view.
    /// </summary>
    private IPublishedContent? FindSettingsNode()
        => contentQuery
            .ContentAtRoot()
            .SelectMany(root => root.ChildrenOfType("settings"))
            .FirstOrDefault();

    private static int KronorToOre(IPublishedContent settings, string alias, int fallbackOre)
    {
        var kronor = settings.Value<int>(alias);
        return kronor > 0 ? kronor * 100 : fallbackOre;
    }

    private static int PositiveOrDefault(IPublishedContent settings, string alias, int fallback)
    {
        var value = settings.Value<int>(alias);
        return value > 0 ? value : fallback;
    }
}
