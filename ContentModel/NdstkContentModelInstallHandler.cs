using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace NDSTK.ContentModel;

/// <summary>
/// Runs the content model installer once Umbraco is up. A failure here must not take the site
/// down - the backoffice is the place to fix a broken schema - so it is logged and swallowed.
/// </summary>
internal sealed class NdstkContentModelInstallHandler(
    IRuntimeState runtimeState,
    NdstkContentModelInstaller installer,
    NdstkContentSeeder seeder,
    NdstkMemberPages memberPages,
    NdstkMemberContentUpgrade memberContentUpgrade,
    NdstkInstructorBackfill instructorBackfill,
    NdstkMemberAccessInstaller memberAccess,
    ILogger<NdstkContentModelInstallHandler> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level is not RuntimeLevel.Run)
        {
            logger.LogInformation(
                "Skipping the NDSTK content model install; runtime level is {Level}.", runtimeState.Level);
            return;
        }

        try
        {
            await installer.InstallAsync();
            seeder.Seed();

            // After the seeder, so a brand new site has its start page to hang these off.
            memberPages.Install();

            // After the pages exist, so the Settings pickers have something to point at.
            memberContentUpgrade.Upgrade();

            // After the Tränare folder exists, since that is where the coach nodes go.
            await instructorBackfill.RunAsync();

            // Last: the portal node has to exist before it can be protected.
            await memberAccess.InstallAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Installing the NDSTK content model failed.");
        }
    }
}
