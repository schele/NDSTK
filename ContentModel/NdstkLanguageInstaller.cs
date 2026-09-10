using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace NDSTK.ContentModel;

/// <summary>
/// Swedish, and only Swedish: the default language, with the en-US that the Umbraco installer
/// creates removed.
/// </summary>
/// <remarks>
/// Unlike the rest of the installer this step deletes something, so it is guarded by a marker in
/// the key/value store and runs exactly once. Without that guard an en-US language re-added later
/// by hand would silently disappear on the next restart.
/// </remarks>
internal sealed class NdstkLanguageInstaller(
    ILanguageService languageService,
    IKeyValueService keyValueService,
    ILogger<NdstkLanguageInstaller> logger)
{
    private const string StateKey = "NDSTK/Languages";
    private const string StateValue = "sv-only";

    private static readonly Guid UserKey = Constants.Security.SuperUserKey;

    public async Task InstallAsync()
    {
        if (keyValueService.GetValue(StateKey) == StateValue)
        {
            return;
        }

        // Swedish has to become the default before en-US can go: Umbraco refuses to delete the
        // default language.
        await EnsureAsync("sv", "Swedish", isDefault: true);
        await RemoveAsync("en-US");

        // en-GB is deliberately neither created nor removed. Not created, because a fresh install
        // should come up single-language now that the switcher is gone. Not removed, because a
        // site that already has it may have dictionary translations against it, and deleting a
        // language takes its content with it - that is a decision for whoever runs the site, made
        // in the backoffice where the consequences are visible, not silently on a restart.

        keyValueService.SetValue(StateKey, StateValue);
        logger.LogInformation("NDSTK languages configured: sv is the default.");
    }

    private async Task EnsureAsync(string isoCode, string cultureName, bool isDefault)
    {
        ILanguage? existing = await languageService.GetAsync(isoCode);

        if (existing is null)
        {
            var language = new Language(isoCode, cultureName) { IsDefault = isDefault };

            var created = await languageService.CreateAsync(language, UserKey);
            if (created.Success is false)
            {
                throw new InvalidOperationException($"Could not create language '{isoCode}': {created.Status}.");
            }

            logger.LogInformation("Created language {IsoCode} (default: {IsDefault}).", isoCode, isDefault);
            return;
        }

        if (isDefault is false || existing.IsDefault)
        {
            return;
        }

        existing.IsDefault = true;

        var updated = await languageService.UpdateAsync(existing, UserKey);
        if (updated.Success is false)
        {
            throw new InvalidOperationException($"Could not make '{isoCode}' the default language: {updated.Status}.");
        }

        logger.LogInformation("Language {IsoCode} is now the default.", isoCode);
    }

    private async Task RemoveAsync(string isoCode)
    {
        ILanguage? existing = await languageService.GetAsync(isoCode);
        if (existing is null)
        {
            return;
        }

        if (existing.IsDefault)
        {
            logger.LogWarning("Not removing language {IsoCode} because it is still the default.", isoCode);
            return;
        }

        var deleted = await languageService.DeleteAsync(isoCode, UserKey);
        if (deleted.Success is false)
        {
            logger.LogWarning("Could not remove language {IsoCode}: {Status}.", isoCode, deleted.Status);
            return;
        }

        logger.LogInformation("Removed language {IsoCode}.", isoCode);
    }
}
