using Microsoft.Extensions.Logging;
using NDSTK.Booking.Domain;
using Umbraco.Cms.Core;
using static NDSTK.ContentModel.NdstkKeys;

namespace NDSTK.ContentModel;

/// <summary>
/// Ensures the member area's pages exist.
/// </summary>
/// <remarks>
/// These go through <see cref="NdstkPageInstaller"/> rather than <see cref="NdstkContentSeeder"/>
/// because the seeder only fills a completely empty tree - on the live site it returns immediately,
/// so a page declared there would never appear. Copy is set only when the page is created, so an
/// editor's rewording is never overwritten on the next restart.
/// </remarks>
internal sealed class NdstkMemberPages(
    NdstkPageInstaller pages,
    ILogger<NdstkMemberPages> logger)
{
    public void Install()
    {
        pages.EnsurePage(
            Nodes.MemberRegister, "Bli medlem", Nodes.Start, "memberRegister", page =>
            {
                page.SetValue("heading", "Bli medlem i NDSTK");
                page.SetValue("description",
                    "Skapa ett konto för att boka träningar. Årsavgiften är 150 kr och betalas "
                    + "första gången du bokar en klass.");
            });

        pages.EnsurePage(
            Nodes.MemberVerify, "Verifiera e-post", Nodes.Start, "memberVerify", page =>
            {
                page.SetValue("heading", "Bekräfta din e-postadress");

                // Nobody should reach this page from a search result - it is only ever the target of
                // a one-time link, and the URL carries a token.
                page.SetValue("metaRobots", "[\"NOINDEX,NOFOLLOW\"]");
            });

        pages.EnsurePage(
            Nodes.MemberPortal, "Mina sidor", Nodes.Start, "memberPortal", page =>
            {
                page.SetValue("heading", "Mina sidor");
                page.SetValue("description", "Dina bokningar och de träningar du kan boka.");

                // Member-only content has no business in a search index.
                page.SetValue("metaRobots", "[\"NOINDEX,NOFOLLOW\"]");
            });

        pages.EnsurePage(
            Nodes.SwishPayment, "Betalning", Nodes.MemberPortal, "swishPayment", page =>
            {
                page.SetValue("heading", "Betala med Swish");
                page.SetValue("metaRobots", "[\"NOINDEX,NOFOLLOW\"]");
            });

        pages.EnsurePage(Nodes.TrainingClasses, "Träningar", Nodes.Start, "trainingClasses");

        pages.EnsurePage(Nodes.Instructors, "Tränare", Nodes.Start, "instructors");

        // Before the classes: each example class picks one of these, so the picker has something to
        // resolve the moment the class is created.
        SeedExampleCoaches();
        SeedExampleClasses();

        logger.LogDebug("Member pages checked.");
    }

    /// <summary>
    /// The two coaches the example classes are assigned to.
    /// </summary>
    /// <remarks>
    /// Names only. The photo, role, quote and merits are what an editor fills in, and inventing
    /// biographies for two fictional coaches would put words in the club's mouth - a coach with
    /// nothing but a name renders as plain text, which is the honest state for seed data.
    /// </remarks>
    private void SeedExampleCoaches()
    {
        pages.EnsurePage(Nodes.ExampleCoach1, "Anna Lind", Nodes.Instructors, "instructor");
        pages.EnsurePage(Nodes.ExampleCoach2, "Johan Berg", Nodes.Instructors, "instructor");
    }

    /// <summary>
    /// Three example classes, so the portal has something to show before an editor has added
    /// anything - the same reason <see cref="NdstkContentSeeder"/> seeds example articles. They are
    /// create-once by key, so deleting them in the backoffice is permanent.
    /// </summary>
    private void SeedExampleClasses()
    {
        // Times are what an editor would type: Swedish local time. TrainingClassService converts to
        // UTC on the way out, so seeding in local time is what keeps the two consistent.
        DateTime todaySwedish = SwedishTime.ToSwedish(DateTime.UtcNow).Date;

        (Guid Key, string Name, int DaysAhead, int Hour, int Capacity, Guid Coach, string Court, string About)[] examples =
        [
            (Nodes.ExampleClass1, "Nybörjartennis", 2, 18, 8, Nodes.ExampleCoach1, "Bana 1",
                "Grunderna i slag och fotarbete. Racket finns att låna."),
            (Nodes.ExampleClass2, "Teknikpass", 4, 19, 6, Nodes.ExampleCoach2, "Bana 2",
                "Vi filar på forehand och backhand i högre tempo."),
            (Nodes.ExampleClass3, "Dubbelträning", 6, 17, 4, Nodes.ExampleCoach1, "Bana 1",
                "Positionsspel och kommunikation i dubbel."),
        ];

        foreach (var example in examples)
        {
            pages.EnsurePage(
                example.Key, example.Name, Nodes.TrainingClasses, "trainingClass", page =>
                {
                    page.SetValue("title", example.Name);
                    page.SetValue("description", example.About);
                    page.SetValue("start", todaySwedish.AddDays(example.DaysAhead).AddHours(example.Hour));
                    page.SetValue("durationMinutes", 60);
                    page.SetValue("capacity", example.Capacity);
                    // A content picker stores a UDI. The coach node is seeded above, so this is
                    // built from the key rather than looked up.
                    page.SetValue(
                        "coach",
                        Udi.Create(Constants.UdiEntityType.Document, example.Coach).ToString());
                    page.SetValue("location", example.Court);
                });
        }
    }
}
