using Microsoft.Extensions.Logging;
using NDSTK.Booking.Services;
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
    NdstkParticipantBackfill backfill,
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
            return;
        }

        try
        {
            // Immediately after the plan, in the same handler, so the ordering is guaranteed: the
            // backfill writes to columns the plan has just added, and swaps an index the plan
            // deliberately left alone. A second notification handler would not guarantee that.
            backfill.Run();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Backfilling NDSTK participants failed.");
        }
    }
}
