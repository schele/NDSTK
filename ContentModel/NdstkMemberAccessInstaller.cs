using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using static NDSTK.ContentModel.NdstkKeys;

namespace NDSTK.ContentModel;

/// <summary>
/// Creates the Medlemmar member group and locks the portal to it.
/// </summary>
/// <remarks>
/// The protection is Umbraco's own public access, not a check inside the portal controller. That
/// matters: the pipeline redirects an anonymous visitor before any of our code runs, so there is no
/// route by which a forgotten guard could expose member content. The controller's own null check on
/// the current member is belt and braces, not the gate.
/// </remarks>
internal sealed partial class NdstkMemberAccessInstaller(
    IMemberGroupService memberGroupService,
    IPublicAccessService publicAccessService,
    IContentService contentService,
    ILogger<NdstkMemberAccessInstaller> logger)
{
    internal const string MemberGroupName = "Medlemmar";

    public async Task InstallAsync()
    {
        IMemberGroup group = await EnsureGroupAsync();

        IContent? portal = contentService.GetById(Nodes.MemberPortal);
        if (portal is null)
        {
            logger.LogWarning("The member portal page does not exist yet; skipping public access.");
            return;
        }

        if (publicAccessService.GetEntryForContent(portal) is not null)
        {
            return;
        }

        // Login and error pages are the site's own, so a member who is not signed in lands on the
        // real login form rather than an Umbraco default.
        IContent? login = contentService.GetById(Nodes.Login);
        IContent? error = contentService.GetById(Nodes.Error);

        if (login is null || error is null)
        {
            logger.LogWarning(
                "Cannot protect the member portal: the login or error page is missing.");
            return;
        }

        var entry = new PublicAccessEntry(portal, login, error, []);
        entry.AddRule(group.Name!, Umbraco.Cms.Core.Constants.Conventions.PublicAccess.MemberRoleRuleType);

        Attempt<OperationResult?> attempt = publicAccessService.Save(entry);
        if (attempt.Success is false)
        {
            logger.LogError("Could not protect the member portal: {Result}.", attempt.Result);
            return;
        }

        logger.LogInformation("Member portal is now restricted to the '{Group}' group.", group.Name);
    }

    private async Task<IMemberGroup> EnsureGroupAsync()
    {
        IMemberGroup? existing = await memberGroupService.GetByNameAsync(MemberGroupName);
        if (existing is not null)
        {
            return existing;
        }

        var group = new MemberGroup { Name = MemberGroupName };

        Attempt<IMemberGroup?, MemberGroupOperationStatus> created =
            await memberGroupService.CreateAsync(group);

        if (created.Success is false)
        {
            throw new InvalidOperationException(
                $"Could not create the '{MemberGroupName}' member group: {created.Status}.");
        }

        logger.LogInformation("Created the '{Group}' member group.", MemberGroupName);
        return created.Result!;
    }
}
