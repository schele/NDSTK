using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace NDSTK.ContentModel;

/// <summary>
/// Creates the pages a feature needs, if they are not there already.
/// </summary>
/// <remarks>
/// This is deliberately not part of <see cref="NdstkContentSeeder"/>. The seeder fills a brand new
/// site and then does nothing at all once the content tree has anything in it, which is right for
/// demo content but wrong for a page a later feature depends on - that page would exist only on a
/// fresh database and never on the live site. This runs on every start instead, and matches by
/// key, so renaming or moving a page in the backoffice will not produce a duplicate.
/// </remarks>
internal sealed class NdstkPageInstaller(
    IContentService contentService,
    ILogger<NdstkPageInstaller> logger)
{
    // IContentService still only takes an integer user id, matching NdstkContentSeeder.
#pragma warning disable CS0618
    private const int UserId = Constants.Security.SuperUserId;
#pragma warning restore CS0618

    private static readonly string[] AllCultures = ["*"];

    /// <summary>
    /// Returns the existing page untouched, or creates and publishes it. Returns null when the
    /// parent does not exist yet, which happens on a database so fresh that even the start page
    /// has not been seeded - the next start will pick it up.
    /// </summary>
    public IContent? EnsurePage(
        Guid key,
        string name,
        Guid parentKey,
        string documentTypeAlias,
        Action<IContent>? configureNew = null)
    {
        IContent? existing = contentService.GetById(key);
        if (existing is not null)
        {
            return existing;
        }

        IContent? parent = contentService.GetById(parentKey);
        if (parent is null)
        {
            logger.LogWarning(
                "Cannot create the '{Name}' page: parent {ParentKey} does not exist yet.", name, parentKey);
            return null;
        }

        IContent page = contentService.Create(name, parent.Id, documentTypeAlias, UserId);
        page.Key = key;
        configureNew?.Invoke(page);
        contentService.Save(page, UserId);

        PublishResult result = contentService.Publish(page, AllCultures, UserId);
        if (result.Success is false)
        {
            logger.LogWarning(
                "Created the '{Name}' page but could not publish it: {Status}.", name, result.Result);
        }
        else
        {
            logger.LogInformation("Created and published the '{Name}' page.", name);
        }

        return page;
    }
}
