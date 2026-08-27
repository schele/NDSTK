using System.Text.Json;
using Esatto.Umbraco.Backoffice.CookieBanner;
using Microsoft.Extensions.Options;
using NDSTK.CookieScan.Core;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;

namespace NDSTK.CookieScan;

/// <summary>
/// Appends scanner-found declarations to the cookie policy page's Block List.
/// </summary>
/// <remarks>
/// Append-only, and scoped to one property of one node. Nothing here updates or deletes an
/// existing block: the purpose text on a declaration is legal wording an editor may have written
/// by hand, and a tool that silently rewrote it would be worse than no tool.
/// <para>
/// The save is deliberately not a publish. A placeholder purpose on an unrecognised cookie must
/// not become public legal text without a human reading it.
/// </para>
/// </remarks>
public sealed class CookieScanWriter(
    IContentService contentService,
    IContentTypeService contentTypeService,
    IEntityService entityService,
    IJsonSerializer jsonSerializer,
    IOptions<CookieBannerOptions> options,
    ILogger<CookieScanWriter> logger)
{
    private const string PolicyAlias = "cookiePolicy";
    private const string DefinitionAlias = "cookieDefinition";
    private const string CookiesProperty = "cookies";
    private const int ScanPageSize = 100;

    // IContentService still only takes an integer user id, same constraint the CookieBanner
    // seeder documents.
#pragma warning disable CS0618
    private const int UserId = Constants.Security.SuperUserId;
#pragma warning restore CS0618

    private static readonly HashSet<string> Categories =
        new(["necessary", "preferences", "statistics", "marketing"], StringComparer.Ordinal);

    private static readonly HashSet<string> StorageTypes =
        new(["Cookie", "localStorage", "sessionStorage", "Pixel"], StringComparer.Ordinal);

    /// <summary>Thrown for anything the caller could fix; the controller turns it into a 400.</summary>
    public sealed class RejectedException(string message) : Exception(message);

    public CookieScanMergeResponse Merge(CookieScanMergeRequest request)
    {
        Validate(request);

        IContentType definitionType = contentTypeService.Get(DefinitionAlias)
            ?? throw new RejectedException(
                $"No '{DefinitionAlias}' element type exists. The CookieBanner package installs it "
                + "on first start at RuntimeLevel.Run - check the logs for CookieBannerInstallHandler.");

        IContent page = ResolvePolicyPage();

        BlockListValue existing = ReadBlockList(page);
        List<string> declaredNames = DeclaredNames(existing);

        // The catalogue here is only used for the plan's ExpectedButNotObserved list, which this
        // response deliberately does not return: that depends on the scanner's own catalogue,
        // which may be an override file this site knows nothing about.
        MergePlan plan = MergePlanner.Plan(
            request.Declarations.Select(ToCandidate), declaredNames, CookieCatalogue.Default());

        if (plan.ExceedsCap)
        {
            throw new RejectedException(
                $"The scan proposes {plan.ToAdd.Count} new declarations, over the limit of "
                + $"{MergePlanner.MaxBlocksPerCall}. Nothing was written: past this many, something "
                + "is wrong with the scan or the catalogue, and adding only the first "
                + $"{MergePlanner.MaxBlocksPerCall} would leave the page in a state nobody chose.");
        }

        if (plan.HasWork is false || request.DryRun)
        {
            return Response(plan, page.Key, saved: false);
        }

        Append(existing, plan, definitionType.Key);

        page.SetValue(CookiesProperty, jsonSerializer.Serialize(existing));

        // Save, never Publish. The editor reviews the new blocks and publishes.
        contentService.Save(page, UserId);

        logger.LogInformation(
            "Cookie scan appended {Count} declaration(s) to the policy page as a draft: {Names}",
            plan.ToAdd.Count,
            string.Join(", ", plan.ToAdd.Select(candidate => candidate.Name)));

        return Response(plan, page.Key, saved: true);
    }

    private void Validate(CookieScanMergeRequest request)
    {
        if (request.Declarations.Count == 0)
        {
            throw new RejectedException("The request contains no declarations.");
        }

        foreach (CookieScanDeclaration declaration in request.Declarations)
        {
            if (string.IsNullOrWhiteSpace(declaration.Name))
            {
                throw new RejectedException("A declaration has a blank cookie name.");
            }

            // Rejected rather than defaulted: an unknown category written to the page would show a
            // cookie as needing no consent while the gating code would never grant it.
            if (Categories.Contains(declaration.Category) is false)
            {
                throw new RejectedException(
                    $"'{declaration.Category}' is not a consent category. Expected one of: "
                    + string.Join(", ", Categories));
            }

            if (StorageTypes.Contains(declaration.StorageType) is false)
            {
                throw new RejectedException(
                    $"'{declaration.StorageType}' is not a storage type. Expected one of: "
                    + string.Join(", ", StorageTypes));
            }
        }
    }

    /// <summary>
    /// Finds the policy page: the configured key when set, otherwise the first published node of
    /// the policy document type.
    /// </summary>
    /// <remarks>
    /// The package's own resolver is internal, so this repeats its rule rather than calling it -
    /// though only for the fallback scan below. For the configured-key case this checks existence
    /// and the content type alias only, unlike the package's resolver, which reads through the
    /// published cache and so answers nothing for an unpublished override; an operator who set
    /// <c>PolicyPageKey</c> explicitly is assumed to mean it regardless of publish state.
    /// <para>
    /// Note the deliberate absence of <c>contentService.GetById(Guid)</c>: Umbraco 18.1.1 declares
    /// only the int overload on IContentService, so the key is resolved through IEntityService
    /// first - which is identical across 17 and 18.
    /// </para>
    /// </remarks>
    private IContent ResolvePolicyPage()
    {
        if (options.Value.PolicyPageKey is Guid configured)
        {
            Attempt<int> id = entityService.GetId(configured, UmbracoObjectTypes.Document);

            IContent? byKey = id.Success ? contentService.GetById(id.Result) : null;

            return byKey is not null && byKey.ContentType.Alias == PolicyAlias
                ? byKey
                : throw new RejectedException(
                    $"Esatto:CookieBanner:PolicyPageKey points at {configured}, which does not "
                    + $"resolve to a '{PolicyAlias}' node.");
        }

        IContentType policyType = contentTypeService.Get(PolicyAlias)
            ?? throw new RejectedException($"No '{PolicyAlias}' document type exists.");

        // IContentService.GetPagedOfType declares `filter` as non-nullable on the interface even
        // though passing null for "no filter" is the documented, supported usage - an annotation
        // mismatch in the shipped API, not a real nullability risk here. The CookieBanner
        // package's own resolver suppresses the identical warning at the identical call.
#pragma warning disable CS8625
        List<IContent> candidates =
            [.. contentService.GetPagedOfType(policyType.Id, 0, ScanPageSize, out _, null, null)];
#pragma warning restore CS8625

        // Prefer a published node, matching the package's own resolver, which reads through the
        // published cache and so never returns an unpublished one. A site with more than one
        // policy page and the wrong one published-first would otherwise have the scan silently
        // append to a node the banner never links to.
        IContent? found = candidates.FirstOrDefault(candidate => candidate.Published);

        if (found is null && candidates.Count > 0)
        {
            // Not a failure: Merge only ever saves, never publishes, so writing to an unpublished
            // draft is a legitimate outcome here - it just should not happen without a word about
            // it, since the banner will not show these declarations until that page is published.
            found = candidates[0];

            logger.LogWarning(
                "No published '{Alias}' node was found; appending to the first unpublished one "
                + "instead ({Key}).",
                PolicyAlias,
                found.Key);
        }

        return found ?? throw new RejectedException(
            $"No '{PolicyAlias}' node exists. The CookieBanner package seeds one on first start.");
    }

    private BlockListValue ReadBlockList(IContent page)
    {
        string? raw = page.GetValue<string>(CookiesProperty);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new BlockListValue
            {
                Layout = new Dictionary<string, IEnumerable<IBlockLayoutItem>>(),
                ContentData = [],
                SettingsData = [],
                Expose = [],
            };
        }

        // IJsonSerializer.Deserialize throws JsonException on a corrupt value rather than
        // returning null - it returns null only for the literal string "null" - so the guard
        // below is a belt-and-braces check, and this catch is what actually turns a malformed
        // 'cookies' value into the 400 this message promises instead of an unhandled 500.
        const string corruptMessage =
            "The policy page's 'cookies' value could not be read as a Block List. Refusing to "
            + "overwrite it - open the page in the backoffice and check it saves cleanly first.";

        try
        {
            return jsonSerializer.Deserialize<BlockListValue>(raw)
                ?? throw new RejectedException(corruptMessage);
        }
        catch (JsonException)
        {
            throw new RejectedException(corruptMessage);
        }
    }

    private static List<string> DeclaredNames(BlockListValue value)
        => [.. value.ContentData
            .SelectMany(block => block.Values)
            .Where(property => property.Alias == "cookieName")
            .Select(property => property.Value?.ToString())
            .Where(name => string.IsNullOrWhiteSpace(name) is false)
            .Select(name => name!)];

    private void Append(BlockListValue value, MergePlan plan, Guid definitionTypeKey)
    {
        List<IBlockLayoutItem> layout =
            [.. value.Layout.TryGetValue(Constants.PropertyEditors.Aliases.BlockList, out IEnumerable<IBlockLayoutItem>? items)
                ? items
                : []];

        foreach (CookieDeclarationCandidate candidate in plan.ToAdd)
        {
            var block = new BlockItemData
            {
                Key = Guid.NewGuid(),
                ContentTypeKey = definitionTypeKey,
                Values =
                [
                    Property("cookieName", candidate.Name),
                    Property("provider", candidate.Provider),

                    // The flexible dropdown always stores an array, even in single-value mode.
                    Property("category", Dropdown(candidate.Category)),
                    Property("purpose", candidate.Purpose),
                    Property("duration", candidate.Duration),
                    Property("storageType", Dropdown(candidate.StorageType)),
                ],
            };

            value.ContentData.Add(block);
            layout.Add(new BlockListLayoutItem(block.Key));

            // Expose is what marks a block visible. Omit it and the block saves and then does not
            // render, with no error anywhere - the failure mode the package's own seeder warns of.
            value.Expose.Add(new BlockItemVariation(block.Key, null, null));
        }

        value.Layout[Constants.PropertyEditors.Aliases.BlockList] = layout;
    }

    private static BlockPropertyValue Property(string alias, object value)
        => new() { Alias = alias, Value = value };

    private string Dropdown(string value) => jsonSerializer.Serialize(new[] { value });

    private static CookieDeclarationCandidate ToCandidate(CookieScanDeclaration declaration)
        => new(
            declaration.Name,
            declaration.Provider,
            declaration.Category,
            declaration.Purpose,
            declaration.Duration,
            declaration.StorageType,
            CandidateFlag.None,
            ConsentPass.Undecided,
            string.Empty);

    private static CookieScanMergeResponse Response(MergePlan plan, Guid pageKey, bool saved)
        => new(
            [.. plan.ToAdd.Select(candidate => candidate.Name)],
            plan.AlreadyDeclared,
            plan.DeclaredButNotFound,
            pageKey,
            saved);
}
