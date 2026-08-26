using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using static NDSTK.ContentModel.NdstkKeys;

namespace NDSTK.ContentModel;

/// <summary>
/// Turns the coach names already typed on the classes into Tränare nodes, points each class at the
/// right one, and then deletes the text field they came from.
/// </summary>
/// <remarks>
/// The coach used to be a line of text on every class, so the same person was spelled out once per
/// class with nothing tying the copies together. This creates one node per distinct name and links
/// the classes to it, leaving an editor to add the photo, quote and merits once rather than
/// re-entering three names by hand.
///
/// Carrying the names over is guarded by a marker in the key/value store and happens once, the same
/// pattern <see cref="NdstkMemberContentUpgrade"/> and the booking backfills use. Retiring the field
/// is not guarded by the marker: it has to happen on a site that migrated before the field was ever
/// going to be deleted, and asking whether the property still exists is a cheaper guard than a
/// second marker.
///
/// The two belong in one class because the order matters. The field is the input to the migration
/// and there is exactly one place that can know the migration is finished with it. Once no database
/// in use still carries the field, this whole class can go.
/// </remarks>
internal sealed class NdstkInstructorBackfill(
    NdstkContentTypeFactory factory,
    IContentService contentService,
    IKeyValueService keyValueService,
    ILogger<NdstkInstructorBackfill> logger)
{
    private const string StateKey = "NDSTK/InstructorBackfill";
    private const string StateValue = "coach-nodes-v1";

    /// <summary>The retired text field, replaced by the <c>coach</c> picker.</summary>
    private const string LegacyAlias = "instructor";

#pragma warning disable CS0618 // IContentService still only takes an integer user id.
    private const int UserId = Constants.Security.SuperUserId;
#pragma warning restore CS0618

    private static readonly string[] AllCultures = ["*"];

    public async Task RunAsync()
    {
        if (keyValueService.GetValue(StateKey) != StateValue)
        {
            CarryNamesOver();
        }

        // Once every name is a node, nothing reads the text field - and an editor should stop seeing
        // a "Tränare (utgått)" box on every class. Deleting a property type deletes its values, so
        // this can only run after the loop above has had its turn.
        if (await factory.RemovePropertyAsync(DocumentTypes.TrainingClass, LegacyAlias))
        {
            logger.LogInformation(
                "Removed the retired coach text field from the training class document type.");
        }
    }

    private void CarryNamesOver()
    {
        IContent? folder = contentService.GetById(Nodes.Instructors);
        if (folder is null)
        {
            // The page installer runs first and creates it. If it is missing something is wrong
            // enough that inventing a second folder would only make it harder to see.
            logger.LogWarning("The Tränare folder is missing; leaving the coach names as they are.");
            return;
        }

        IContent? classes = contentService.GetById(Nodes.TrainingClasses);
        if (classes is null)
        {
            keyValueService.SetValue(StateKey, StateValue);
            return;
        }

        // Case-insensitive and culture-aware, so "anna lind" and "Anna Lind" are one coach rather
        // than two. Ordinal comparison would treat them as different people.
        Dictionary<string, IContent> byName = new(StringComparer.CurrentCultureIgnoreCase);

        foreach (IContent existing in ChildrenOf(folder))
        {
            byName[existing.Name ?? string.Empty] = existing;
        }

        var created = 0;
        var linked = 0;

        foreach (IContent trainingClass in ChildrenOf(classes))
        {
            var name = trainingClass.GetValue<string>(LegacyAlias)?.Trim();

            // Nothing to carry over, or an editor has already picked somebody.
            if (string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(trainingClass.GetValue<string>("coach")) is false)
            {
                continue;
            }

            if (byName.TryGetValue(name, out IContent? coach) is false)
            {
                coach = contentService.Create(name, folder.Id, "instructor", UserId);
                contentService.Save(coach, UserId);
                Publish(coach);

                byName[name] = coach;
                created++;
            }

            // A content picker stores a UDI, which is why this could not be a repoint of the text
            // field: the two store different things in the same column type.
            trainingClass.SetValue(
                "coach", Udi.Create(Constants.UdiEntityType.Document, coach.Key).ToString());

            contentService.Save(trainingClass, UserId);
            Publish(trainingClass);
            linked++;
        }

        keyValueService.SetValue(StateKey, StateValue);

        if (created == 0 && linked == 0)
        {
            logger.LogDebug("No coach names to carry over.");
            return;
        }

        logger.LogInformation(
            "Created {Created} Tränare node(s) from the names on the classes and linked {Linked} class(es).",
            created, linked);
    }

    /// <summary>Every child of a node, in one go.</summary>
    /// <remarks>
    /// There are a dozen classes and a handful of coaches, so paging buys nothing and the whole set
    /// is asked for at once. Templates are not loaded: nothing here renders a page. Every property
    /// is, because a class read here is saved again below and a partly loaded one risks writing back
    /// blanks.
    /// </remarks>
    private IEnumerable<IContent> ChildrenOf(IContent parent) =>
        contentService.GetPagedChildren(
            parent.Id, 0, int.MaxValue, out _,
            propertyAliases: null, filter: null, ordering: null, loadTemplates: false);

    private void Publish(IContent content)
    {
        PublishResult result = contentService.Publish(content, AllCultures, UserId);
        if (result.Success is false)
        {
            logger.LogWarning("Could not publish '{Name}': {Status}.", content.Name, result.Result);
        }
    }
}
