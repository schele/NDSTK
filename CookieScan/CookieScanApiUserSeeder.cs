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
            // Umbraco.Cms.Core.Security.ClientCredentialsManagerBase.SafeClientId (decompiled from
            // Umbraco.Infrastructure.dll 18.1.1, used by BackOfficeUserClientCredentialsManager)
            // silently prepends "umbraco-back-office-" to whatever client id it is given, unless
            // that prefix is already present. BackOfficeController.Token() - the action behind
            // POST .../security/back-office/token - resolves a client_credentials grant by calling
            // IBackOfficeUserClientCredentialsManager.FindUserAsync(request.ClientId), which always
            // applies that same normalisation before querying the user's client-id association. So
            // the association row has to be stored under the *prefixed* form, or that lookup can
            // never find it, no matter what raw string was stored. IUserService.AddClientIdAsync /
            // FindByClientIdAsync know nothing about this convention themselves - they store and
            // query exactly the string they are given. Hence BackOfficeAssociationClientId below,
            // applied only to the association, never to the OpenIddict application registration
            // further down: that registration is matched byte-for-byte against whatever client_id
            // the caller puts on the wire (NDSTK.CookieScanner's --client-id, unprefixed), and
            // EnsureBackOfficeClientCredentialsApplicationAsync stores its argument completely
            // verbatim with no prefix expectations of its own (confirmed by decompiling
            // BackOfficeApplicationManager, which registers the developer-only "umbraco-swagger" /
            // "umbraco-postman" apps the exact same unprefixed way).
            string associationClientId = BackOfficeAssociationClientId(settings.ClientId);

            IUser? existing = await userService.FindByClientIdAsync(associationClientId);

            if (existing is null)
            {
                // A miss here does not mean no user exists. If a previous boot created the user
                // below and then failed to attach the client id (the error path a few lines down),
                // that user is still sitting there under settings.Email, and CreateAsync would fail
                // forever afterwards with a duplicate-email error - the exact stuck loop this
                // lookup exists to avoid. GetByEmail is IMembershipMemberService<IUser>'s lookup,
                // inherited onto IUserService; it returns null rather than throwing when nothing
                // matches.
                IUser? existingByEmail = userService.GetByEmail(settings.Email);

                Guid userKey;

                if (existingByEmail is null)
                {
                    Guid? createdKey = await CreateUserAsync(settings);

                    if (createdKey is null)
                    {
                        return;
                    }

                    userKey = createdKey.Value;
                }
                else
                {
                    userKey = existingByEmail.Key;
                }

                UserClientCredentialsOperationStatus clientIdStatus =
                    await userService.AddClientIdAsync(userKey, associationClientId);

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
            // Not settings.ClientId: Umbraco validates the username as an email address whenever
            // Umbraco:CMS:Security:UsernameIsEmail is true, which is the default. The client id is
            // a separate concept, attached below via AddClientIdAsync - it has nothing to do with
            // the username.
            UserName = settings.Email,
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

    /// <summary>
    /// Reproduces the one normalisation Umbraco's own token-endpoint lookup applies to a client id
    /// before it queries the user↔client-id association - see the long comment in
    /// <see cref="SeedAsync"/> for the full citation and reasoning.
    /// </summary>
    /// <remarks>
    /// This mirrors Umbraco.Cms.Core.Security.ClientCredentialsManagerBase.SafeClientId, which is
    /// not reachable from here: it lives on an internal base class with no public equivalent. If a
    /// future Umbraco upgrade changes that prefix, this literal needs to change with it - re-verify
    /// against Umbraco.Infrastructure.dll for the new version.
    /// </remarks>
    private const string BackOfficeClientIdPrefix = "umbraco-back-office-";

    private static string BackOfficeAssociationClientId(string clientId) =>
        clientId.StartsWith(BackOfficeClientIdPrefix, StringComparison.Ordinal)
            ? clientId
            : BackOfficeClientIdPrefix + clientId;
}
