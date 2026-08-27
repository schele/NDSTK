namespace NDSTK.CookieScan.Core;

/// <summary>
/// Turns one observed entry into a proposed declaration, deciding its category from the consent
/// state it appeared under rather than from a guess at its name.
/// </summary>
public static class CategoryInference
{
    private const string Necessary = "necessary";

    public static CookieDeclarationCandidate Classify(
        ObservedEntry entry,
        CookieCatalogue catalogue,
        DateTimeOffset now,
        Locale locale)
    {
        CatalogueEntry? known = catalogue.Match(entry.Name);

        string category;
        CandidateFlag flag;

        if (known is not null)
        {
            category = known.Category;

            // The rule the whole design exists for. A catalogued category that the pass had not
            // granted means the site set that cookie without permission - whether the visitor
            // refused outright, or granted something else entirely. Necessary is exempt: it is
            // implied rather than granted, so it never appears in a granted set.
            bool granted = category == Necessary
                || ConsentPasses.Granted(entry.FirstSeenPass).Contains(category);

            flag = granted ? CandidateFlag.None : CandidateFlag.Violation;
        }
        else
        {
            string? implied = ConsentPasses.ImpliedCategory(entry.FirstSeenPass);

            // No implied category means accept-all: everything was granted, so the cookie could
            // belong to any of the three and the scan has no evidence for which. The fallback
            // category is a placeholder, which is exactly what NeedsReview announces.
            category = implied ?? catalogue.UnknownCategory;
            flag = implied is null ? CandidateFlag.NeedsReview : CandidateFlag.None;
        }

        return new CookieDeclarationCandidate(
            // Collapsed onto the catalogue pattern, so two Google Analytics properties become one
            // block rather than one per property - and so the next scan recognises them as
            // already declared.
            Name: known?.Pattern ?? entry.Name,
            Provider: known?.Provider.For(locale) ?? Wording.UnknownProvider(locale),
            Category: category,
            Purpose: known?.Purpose.For(locale)
                ?? (flag == CandidateFlag.NeedsReview
                    ? Wording.NeedsReviewPurpose(locale)
                    : Wording.UnknownPurpose(locale)),
            Duration: DurationFormatter.Format(
                entry.Storage, known?.DurationDays, entry.Expires, now, locale),
            StorageType: StorageKinds.ToWireName(entry.Storage),
            Flag: flag,
            FirstSeenPass: entry.FirstSeenPass,
            FirstSeenUrl: entry.FirstSeenUrl);
    }
}
