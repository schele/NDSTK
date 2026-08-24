using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;

namespace NDSTK.Booking.Data.Migrations;

/// <summary>
/// Runs the booking migration plan once Umbraco is up, mirroring how
/// NdstkContentModelInstallHandler installs the content model. A failure is logged rather than
/// thrown: a broken schema should not take the whole site down, and the backoffice is the place
/// to fix it from.
/// </summary>
internal sealed class BookingMigrationRunner(
    IRuntimeState runtimeState,
    IMigrationPlanExecutor migrationPlanExecutor,
    ICoreScopeProvider scopeProvider,
    IKeyValueService keyValueService,
    ILogger<BookingMigrationRunner> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public async Task HandleAsync(
        UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level is not RuntimeLevel.Run)
        {
            logger.LogInformation(
                "Skipping the booking migration; runtime level is {Level}.", runtimeState.Level);
            return;
        }

        try
        {
            var upgrader = new Upgrader(new BookingMigrationPlan());
            await upgrader.ExecuteAsync(migrationPlanExecutor, scopeProvider, keyValueService);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Running the NDSTK booking migration failed.");
        }
    }
}
