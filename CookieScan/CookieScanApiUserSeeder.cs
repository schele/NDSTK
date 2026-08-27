using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;
using Umbraco.Cms.Infrastructure.Security;

namespace NDSTK.CookieScan;

/// <summary>
/// Creates the cookie scanner's API user and registers its client credentials, if configured to.
/// </summary>
/// <remarks>
/// Idempotent: an existing client id means there is nothing to do. Failures are logged and
/// swallowed rather than blocking boot - the same posture the CookieBanner package takes about its
/// own installer, and for the same reason. A missing scanner credential must not take the site down.
/// </remarks>
public sealed class CookieScanApiUserSeeder(
    IUserService userService,
    IUserGroupService userGroupService,
    IBackOfficeApplicationManager applicationManager,
    IOptions<CookieScanApiUserOptions> options,
    ILogger<CookieScanApiUserSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        CookieScanApiUserOptions settings = options.Value;

        if (settings.Enabled is false)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            logger.LogWarning(
                "{Section}:Enabled is true but no ClientSecret is configured, so the cookie "
                + "scanner's API user was not created. Put the secret in appsettings.Secrets.json "
                + "under {Section}:ClientSecret.",
                CookieScanApiUserOptions.SectionName,
                CookieScanApiUserOptions.SectionName);

            return;
        }

        try
        {
            IUser? existing = await userService.FindByClientIdAsync(settings.ClientId);

            if (existing is null)
            {
                Guid? userKey = await CreateUserAsync(settings);

                if (userKey is null)
                {
                    return;
                }

                UserClientCredentialsOperationStatus clientIdStatus =
                    await userService.AddClientIdAsync(userKey.Value, settings.ClientId);

                if (clientIdStatus != UserClientCredentialsOperationStatus.Success)
                {
                    // Never reach the "ready" log below: the user exists but has no client id
                    // attached, so a token request for it would fail later with no clue why.
                    logger.LogError(
                        "Created the cookie scanner's API user but could not attach client id "
                        + "{ClientId} to it: {Status}.",
                        settings.ClientId,
                        clientIdStatus);

                    return;
                }
            }

            // Registers the client id and secret with the OpenIddict application store. Safe to
            // repeat: this is what lets a rotated secret take effect on the next boot.
            await applicationManager.EnsureBackOfficeClientCredentialsApplicationAsync(
                settings.ClientId, settings.ClientSecret, cancellationToken);

            logger.LogInformation(
                "The cookie scanner's API user is ready with client id {ClientId}.",
                settings.ClientId);
        }
        catch (Exception error)
        {
            // Never fatal. The site working matters more than the scanner being able to write.
            logger.LogError(
                error,
                "Could not set up the cookie scanner's API user. The scanner will still run in "
                + "report-only mode.");
        }
    }

    private async Task<Guid?> CreateUserAsync(CookieScanApiUserOptions settings)
    {
        var groupKeys = new HashSet<Guid>();

        foreach (string alias in settings.UserGroupAliases)
        {
            IUserGroup? group = await userGroupService.GetAsync(alias);

            if (group?.Key is Guid key)
            {
                groupKeys.Add(key);
            }
            else
            {
                logger.LogWarning("No user group with alias '{Alias}' exists; skipping it.", alias);
            }
        }

        if (groupKeys.Count == 0)
        {
            logger.LogError(
                "None of the configured user groups ({Aliases}) exist, so the API user was not "
                + "created - a user with no group cannot be authorised for anything.",
                string.Join(", ", settings.UserGroupAliases));

            return null;
        }

        var model = new UserCreateModel
        {
            Kind = UserKind.Api,
            Name = settings.Name,
            UserName = settings.ClientId,
            Email = settings.Email,
            UserGroupKeys = groupKeys,
        };

        Attempt<UserCreationResult, UserOperationStatus> attempt =
            await userService.CreateAsync(Constants.Security.SuperUserKey, model, approveUser: true);

        if (attempt.Success is false)
        {
            logger.LogError(
                "Could not create the cookie scanner's API user: {Status}.", attempt.Status);

            return null;
        }

        return attempt.Result.CreatedUser?.Key;
    }
}
