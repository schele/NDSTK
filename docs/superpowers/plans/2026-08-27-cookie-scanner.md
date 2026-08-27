# Cookie Scanner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a browser-driven cookie scanner that finds every cookie, `localStorage` and `sessionStorage` entry the site actually sets, infers each one's consent category from the consent state it appears under, and appends the missing ones to the cookie policy page's Block List as a draft an editor reviews and publishes.

**Architecture:** Every rule — catalogue matching, category inference, duration wording, merge planning — lives in `NDSTK.CookieScan.Core`, a class library with no Umbraco, Playwright or HTTP dependency, so it is unit-testable without a browser or a published content graph. A console project drives Playwright over the site in six consent passes and posts its findings to one narrow, append-only Management API endpoint in the web project, which does the Umbraco-side merge with Umbraco's own Block List types. Chromium never runs on the production server.

**Tech Stack:** .NET 10, Umbraco CMS 18.1.1, `Esatto.Umbraco.Backoffice.CookieBanner` 1.1.1, `Microsoft.Playwright`, `System.Text.Json`, xUnit.

**Spec:** [docs/superpowers/specs/2026-08-27-cookie-scanner-design.md](../specs/2026-08-27-cookie-scanner-design.md)

## Global Constraints

- **.NET 10, Umbraco 18.1.1.** Nullable reference types enabled, implicit usings enabled, matching every existing project.
- **`IContentService.GetById(Guid)` MUST NOT be called.** Verified against `Umbraco.Core.xml` 18.1.1: only `GetById(System.Int32)` is declared. Resolve a key to an id with `IEntityService.GetId(Guid, UmbracoObjectTypes)` — verified present — then call `GetById(int)`.
- **`CookieBannerKeys` and `CookiePolicyPageResolver` are `internal` to the package.** Site code cannot reference them. Resolve content types by alias with `IContentTypeService.Get(string)` (verified present via `IContentTypeBaseService<T>.Get(System.String)`) and the policy page by paging over its type.
- **`CookieBannerOptions` IS public**, with `SectionName = "Esatto:CookieBanner"`, `CookieName`, `PolicyPageKey`, `EndpointPath`, `ThrottleRequestsPerMinute`. Inject `IOptions<CookieBannerOptions>`; the package's composer already registers it.
- **A new block must be added to `Layout`, `ContentData` AND `Expose`.** The package seeder's comment describes `Expose` as what "marks the blocks as visible". Omit it and the block saves but does not render — silently.
- **`category` and `storageType` are stored as serialized single-element JSON arrays.** The flexible dropdown "always stores an array, even in single-value mode".
- **The write is `contentService.Save(...)`. Never `Publish`.**
- **Consent category wire names are the stable contract:** `necessary`, `preferences`, `statistics`, `marketing` — lowercase, exact. Storage type values are `Cookie`, `localStorage`, `sessionStorage`, `Pixel` — mixed case, exact.
- **Visitor-facing copy is Swedish.** Code identifiers, comments and log messages are English, matching the existing `ContentModel` and `Booking` code.
- **`NDSTK.csproj` sits at the repository root**, so every sibling project directory must be listed in its `DefaultItemExcludes` or the SDK's default globs pull their sources into the web assembly and the build fails with duplicate assembly attributes.
- **The user commits manually.** No task contains a `git commit` step. Each task ends with a verification checkpoint, leaving a clean tree for review.
- **The user starts and stops the site.** No task starts or restarts the app. Where a task needs the site running, it says so and stops for the user to do it.
- **Branch:** `feature/cookie-scanner`, already created.

---

## File Structure

Three new projects plus a feature folder in the web project. `NDSTK.CookieScan.Core` exists for the reason `NDSTK.Domain.csproj` already records: keeping the rules out of the web project makes the absence of an Umbraco dependency a compiler guarantee rather than a matter of discipline, and keeps the test suite runnable while the site holds a file lock on `NDSTK.dll`.

**Two deliberate refinements to the spec, both made here rather than during execution:**

1. The spec's catalogue schema had `"purpose": "…"` as a plain string and `"duration": "24 månader"` as pre-written Swedish. That cannot honour `--locale en`: an English run would emit Swedish durations. Purpose and provider become a `{"sv": …, "en": …}` pair, and `duration` becomes machine-readable `durationDays`, rendered per locale by `DurationFormatter`. Same behaviour, both locales actually correct.
2. The 50-block cap is expressed as `MergePlan.ExceedsCap` in Core so it is unit-testable, with the endpoint reading that property to return its `400`. The spec put the cap only at the endpoint, where a test would need a content graph.

| File | Responsibility |
| --- | --- |
| `NDSTK.CookieScan.Core/NDSTK.CookieScan.Core.csproj` | Pure-rules library, no dependencies |
| `NDSTK.CookieScan.Core/CookieNameMatcher.cs` | `*` glob matching and pattern specificity |
| `NDSTK.CookieScan.Core/Locale.cs` | `Locale` enum and `LocalisedText` |
| `NDSTK.CookieScan.Core/StorageKind.cs` | Storage kinds and their wire names |
| `NDSTK.CookieScan.Core/ConsentPass.cs` | The seven passes, what each grants, what each implies |
| `NDSTK.CookieScan.Core/ObservedEntry.cs` | One thing a scan saw |
| `NDSTK.CookieScan.Core/CatalogueEntry.cs` | One catalogue row |
| `NDSTK.CookieScan.Core/CookieCatalogue.cs` | Catalogue parsing, matching, most-specific-wins |
| `NDSTK.CookieScan.Core/Resources/cookie-catalogue.json` | Embedded default catalogue |
| `NDSTK.CookieScan.Core/DurationFormatter.cs` | Expiry and `durationDays` → visitor-facing text |
| `NDSTK.CookieScan.Core/Wording.cs` | Generated copy for unknown cookies, per locale |
| `NDSTK.CookieScan.Core/CookieDeclarationCandidate.cs` | A declaration the scan proposes |
| `NDSTK.CookieScan.Core/CategoryInference.cs` | Pass → category, plus the violation rule |
| `NDSTK.CookieScan.Core/MergePlan.cs` | The planned outcome of a merge |
| `NDSTK.CookieScan.Core/MergePlanner.cs` | Dedupe, collapse, cap, stale reporting |
| `NDSTK.CookieScanner/NDSTK.CookieScanner.csproj` | Console exe, Playwright + HTTP |
| `NDSTK.CookieScanner/ScanOptions.cs` | Parsed CLI surface |
| `NDSTK.CookieScanner/BrowserBootstrap.cs` | Finds or fetches Chromium |
| `NDSTK.CookieScanner/SiteCrawler.cs` | Bounded same-host URL discovery |
| `NDSTK.CookieScanner/PageCapture.cs` | Cookies + storage + hosts off one page |
| `NDSTK.CookieScanner/ConsentPassRunner.cs` | One pass: decide, replay URLs, capture |
| `NDSTK.CookieScanner/MemberDimension.cs` | Login and the member-area discovery |
| `NDSTK.CookieScanner/ScanReportWriter.cs` | Console table, `.md` and `.json` |
| `NDSTK.CookieScanner/ManagementApiClient.cs` | Token, then the merge post |
| `NDSTK.CookieScanner/Program.cs` | Orchestration and exit codes |
| `CookieScan/CookieScanController.cs` | The one Management API endpoint |
| `CookieScan/CookieScanContracts.cs` | Request and response DTOs |
| `CookieScan/CookieScanWriter.cs` | Umbraco-side Block List merge |
| `CookieScan/CookieScanApiUser.cs` | Options for the scanner's API user |
| `CookieScan/CookieScanApiUserSeeder.cs` | Creates that API user, dev-gated |
| `NDSTK.Tests/CookieNameMatcherTests.cs` | Task 1 |
| `NDSTK.Tests/CookieCatalogueTests.cs` | Task 2 |
| `NDSTK.Tests/DurationFormatterTests.cs` | Task 3 |
| `NDSTK.Tests/CategoryInferenceTests.cs` | Task 4 |
| `NDSTK.Tests/MergePlannerTests.cs` | Task 5 |

---

## Task 1: Core project and `CookieNameMatcher`

**Files:**
- Create: `NDSTK.CookieScan.Core/NDSTK.CookieScan.Core.csproj`
- Create: `NDSTK.CookieScan.Core/CookieNameMatcher.cs`
- Modify: `NDSTK.slnx`
- Modify: `NDSTK.csproj` — the `DefaultItemExcludes` line
- Modify: `NDSTK.Tests/NDSTK.Tests.csproj` — add a `ProjectReference`
- Test: `NDSTK.Tests/CookieNameMatcherTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `NDSTK.CookieScan.Core.CookieNameMatcher` with `static bool Matches(string? pattern, string? name)`, `static bool EitherMatches(string? a, string? b)`, `static int WildcardCharCount(string pattern, string name)`, `static int LiteralPrefixLength(string pattern)`. Tasks 2 and 5 depend on all four.

- [ ] **Step 1: Create the project file**

`NDSTK.CookieScan.Core/NDSTK.CookieScan.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>NDSTK.CookieScan.Core</RootNamespace>
    <!--
      No Umbraco, no Playwright, no HTTP - deliberately, and enforced by having no
      PackageReference at all. Every rule the scanner and the site's merge endpoint share lives
      here so both can be tested without a browser or a published content graph. Same reasoning
      as NDSTK.Domain: a running site holds a file lock on NDSTK.dll, so a test suite that has to
      build the web project cannot run while you are looking at the site.
    -->
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="Resources\cookie-catalogue.json" />
  </ItemGroup>

</Project>
```

The `EmbeddedResource` is declared now, in the one place the project file is written, even though the file itself arrives in Task 2. Add a placeholder so this task builds:

```bash
mkdir -p NDSTK.CookieScan.Core/Resources
echo '{ "unknownCategory": "marketing", "entries": [] }' > NDSTK.CookieScan.Core/Resources/cookie-catalogue.json
```

- [ ] **Step 2: Register the project in the solution**

`NDSTK.slnx` becomes:

```xml
<Solution>
  <Project Path="NDSTK.csproj" />
  <Project Path="NDSTK.CookieScan.Core/NDSTK.CookieScan.Core.csproj" />
  <Project Path="NDSTK.Domain/NDSTK.Domain.csproj" />
  <Project Path="NDSTK.Tests/NDSTK.Tests.csproj" />
</Solution>
```

- [ ] **Step 3: Exclude the new project from the web project's globs**

In `NDSTK.csproj`, the existing line reads:

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);NDSTK.Tests\**;NDSTK.Domain\**</DefaultItemExcludes>
```

Change it to:

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);NDSTK.Tests\**;NDSTK.Domain\**;NDSTK.CookieScan.Core\**;NDSTK.CookieScanner\**</DefaultItemExcludes>
```

`NDSTK.CookieScanner\**` is listed now even though that project arrives in Task 6. Forgetting it later produces a duplicate-assembly-attribute build failure whose message points at neither project, and the comment above that line already states the rule.

- [ ] **Step 4: Reference Core from the test project**

In `NDSTK.Tests/NDSTK.Tests.csproj`, the existing `ItemGroup` becomes:

```xml
  <ItemGroup>
    <ProjectReference Include="..\NDSTK.Domain\NDSTK.Domain.csproj" />
    <ProjectReference Include="..\NDSTK.CookieScan.Core\NDSTK.CookieScan.Core.csproj" />
  </ItemGroup>
```

- [ ] **Step 5: Verify the solution still builds**

Run: `dotnet build NDSTK.CookieScan.Core/NDSTK.CookieScan.Core.csproj`
Expected: build succeeded, 0 errors.

Run: `dotnet build NDSTK.Tests/NDSTK.Tests.csproj`
Expected: build succeeded. If this fails with duplicate assembly attributes, Step 3 was not applied.

- [ ] **Step 6: Write the failing tests**

`NDSTK.Tests/CookieNameMatcherTests.cs`:

```csharp
using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class CookieNameMatcherTests
{
    // The package seeds ".AspNetCore.Antiforgery.*" as a declaration, and ASP.NET Core appends a
    // random suffix to the real cookie. If a scan cannot recognise the pattern it re-adds the
    // cookie on every single run, which is the failure that makes a scanner worse than nothing.
    [Fact]
    public void A_pattern_matches_the_real_generated_name()
    {
        Assert.True(CookieNameMatcher.Matches(
            ".AspNetCore.Antiforgery.*", ".AspNetCore.Antiforgery.CfDJ8Nf_gA"));
    }

    [Fact]
    public void A_literal_name_never_matches_a_different_name()
    {
        Assert.False(CookieNameMatcher.Matches("UMB_MEMBER", "UMB_MEMBER_OTHER"));
        Assert.False(CookieNameMatcher.Matches("_ga", "_gat"));
    }

    [Fact]
    public void A_literal_name_matches_itself()
    {
        Assert.True(CookieNameMatcher.Matches("UMB_MEMBER", "UMB_MEMBER"));
    }

    // Cookie names are compared case-sensitively by browsers but declared by hand in the
    // backoffice. A casing near-miss should count as already declared, not as a new cookie.
    [Fact]
    public void Matching_ignores_case()
    {
        Assert.True(CookieNameMatcher.Matches("umb_member", "UMB_MEMBER"));
        Assert.True(CookieNameMatcher.Matches(".ASPNETCORE.ANTIFORGERY.*", ".aspnetcore.antiforgery.x"));
    }

    // The merge compares a found name against a declared one without knowing which side carries
    // the wildcard: the catalogue collapses onto patterns, but an editor may have typed a literal.
    [Fact]
    public void EitherMatches_works_whichever_side_carries_the_wildcard()
    {
        Assert.True(CookieNameMatcher.EitherMatches("_ga_*", "_ga_ABC123"));
        Assert.True(CookieNameMatcher.EitherMatches("_ga_ABC123", "_ga_*"));
        Assert.False(CookieNameMatcher.EitherMatches("_ga_*", "_fbp"));
    }

    [Fact]
    public void A_bare_wildcard_matches_anything_non_empty()
    {
        Assert.True(CookieNameMatcher.Matches("*", "anything"));
    }

    [Fact]
    public void Multiple_wildcards_are_supported()
    {
        Assert.True(CookieNameMatcher.Matches("_hj*Session*", "_hjFirstSessionUser"));
        Assert.False(CookieNameMatcher.Matches("_hj*Session*", "_hjUser"));
    }

    // A blank on either side is editor noise or a capture bug; it must never match, or one empty
    // declaration would swallow every found cookie and the scan would report nothing new forever.
    [Theory]
    [InlineData(null, "UMB_MEMBER")]
    [InlineData("", "UMB_MEMBER")]
    [InlineData("   ", "UMB_MEMBER")]
    [InlineData("UMB_MEMBER", null)]
    [InlineData("UMB_MEMBER", "")]
    public void A_blank_on_either_side_never_matches(string? pattern, string? name)
    {
        Assert.False(CookieNameMatcher.Matches(pattern, name));
    }

    // The catalogue picks between competing patterns by how much each leaves to a wildcard, so
    // "_ga_*" must beat "_ga*" must beat "*" for a real Google Analytics property cookie.
    [Fact]
    public void Wildcard_span_orders_competing_patterns_by_specificity()
    {
        const string name = "_ga_ABC123";

        int specific = CookieNameMatcher.WildcardCharCount("_ga_*", name);
        int looser = CookieNameMatcher.WildcardCharCount("_ga*", name);
        int loosest = CookieNameMatcher.WildcardCharCount("*", name);

        Assert.True(specific < looser);
        Assert.True(looser < loosest);
    }

    [Fact]
    public void Literal_prefix_length_breaks_a_tie()
    {
        Assert.Equal(4, CookieNameMatcher.LiteralPrefixLength("_ga_*"));
        Assert.Equal(0, CookieNameMatcher.LiteralPrefixLength("*"));
        Assert.Equal(10, CookieNameMatcher.LiteralPrefixLength("UMB_MEMBER"));
    }
}
```

- [ ] **Step 7: Run the tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter CookieNameMatcherTests`
Expected: build failure, `CS0103: The name 'CookieNameMatcher' does not exist`.

- [ ] **Step 8: Write the implementation**

`NDSTK.CookieScan.Core/CookieNameMatcher.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>
/// Matches cookie names against declaration patterns, where <c>*</c> is the only wildcard.
/// </summary>
/// <remarks>
/// The CookieBanner package seeds pattern declarations - <c>.AspNetCore.Antiforgery.*</c> - and
/// ASP.NET Core appends a random suffix to the real cookie, so a found name has to be recognisable
/// by the pattern already on the page. Without that, every scan re-adds a cookie that is already
/// declared, and the tool actively makes the policy page worse.
/// <para>
/// Case-insensitive on purpose. Browsers compare cookie names case-sensitively, but declarations
/// are typed by hand, and a casing near-miss should count as declared rather than as new.
/// </para>
/// </remarks>
public static class CookieNameMatcher
{
    /// <summary>
    /// True when <paramref name="name"/> matches <paramref name="pattern"/>. A blank on either
    /// side is never a match: one empty declaration would otherwise swallow every found cookie.
    /// </summary>
    public static bool Matches(string? pattern, string? name)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return IsMatch(pattern, name);
    }

    /// <summary>
    /// True when either string, read as a pattern, matches the other. The merge compares a found
    /// name against a declared one without knowing which of the two carries the wildcard.
    /// </summary>
    public static bool EitherMatches(string? a, string? b)
        => Matches(a, b) || Matches(b, a);

    /// <summary>
    /// How many characters of <paramref name="name"/> the pattern's wildcards had to absorb.
    /// Lower is more specific, which is how the catalogue chooses between two matching entries.
    /// </summary>
    public static int WildcardCharCount(string pattern, string name)
    {
        int wildcards = pattern.Count(character => character == '*');
        int literals = pattern.Length - wildcards;

        return name.Length - literals;
    }

    /// <summary>
    /// Characters before the first wildcard. The tie-break when two patterns absorb the same
    /// number of characters.
    /// </summary>
    public static int LiteralPrefixLength(string pattern)
    {
        int star = pattern.IndexOf('*', StringComparison.Ordinal);

        return star < 0 ? pattern.Length : star;
    }

    // Iterative glob rather than a translated Regex: the pattern comes from an editable JSON
    // catalogue and from hand-typed declarations, so a stray '(' or '+' must be a literal
    // character rather than a regex construct - or worse, a parse exception mid-scan.
    private static bool IsMatch(string pattern, string name)
    {
        int patternIndex = 0;
        int nameIndex = 0;
        int lastStar = -1;
        int nameAtLastStar = 0;

        while (nameIndex < name.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                lastStar = patternIndex++;
                nameAtLastStar = nameIndex;
            }
            else if (patternIndex < pattern.Length && SameCharacter(pattern[patternIndex], name[nameIndex]))
            {
                patternIndex++;
                nameIndex++;
            }
            else if (lastStar >= 0)
            {
                // Backtrack: let the last wildcard absorb one more character and try again.
                patternIndex = lastStar + 1;
                nameIndex = ++nameAtLastStar;
            }
            else
            {
                return false;
            }
        }

        // Trailing wildcards may legitimately match nothing at all.
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static bool SameCharacter(char pattern, char name)
        => char.ToLowerInvariant(pattern) == char.ToLowerInvariant(name);
}
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter CookieNameMatcherTests`
Expected: every test in the class passes.

- [ ] **Step 10: Verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded, 0 errors, 0 warnings.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: every pre-existing test still passes.
Run: `git status --short` — expected: exactly the six files from this task, nothing else.

Leave the tree for the user to review and commit.

---

## Task 2: Locale, storage kinds, passes, and the catalogue

**Files:**
- Create: `NDSTK.CookieScan.Core/Locale.cs`
- Create: `NDSTK.CookieScan.Core/StorageKind.cs`
- Create: `NDSTK.CookieScan.Core/ConsentPass.cs`
- Create: `NDSTK.CookieScan.Core/ObservedEntry.cs`
- Create: `NDSTK.CookieScan.Core/CatalogueEntry.cs`
- Create: `NDSTK.CookieScan.Core/CookieCatalogue.cs`
- Modify: `NDSTK.CookieScan.Core/Resources/cookie-catalogue.json` — replace the Task 1 placeholder
- Test: `NDSTK.Tests/CookieCatalogueTests.cs`

**Interfaces:**
- Consumes: `CookieNameMatcher.Matches`, `.WildcardCharCount`, `.LiteralPrefixLength` from Task 1.
- Produces:
  - `enum Locale { Sv, En }` and `sealed record LocalisedText(string Sv, string En)` with `string For(Locale locale)`
  - `enum StorageKind { Cookie, LocalStorage, SessionStorage }` and `static class StorageKinds` with `string ToWireName(StorageKind)`
  - `enum ConsentPass { Undecided, RejectAll, Preferences, Statistics, Marketing, AcceptAll, MemberArea }` and `static class ConsentPasses` with `IReadOnlyList<ConsentPass> Comparable`, `IReadOnlySet<string> Granted(ConsentPass)`, `string? ImpliedCategory(ConsentPass)`
  - `sealed record ObservedEntry(string Name, StorageKind Storage, ConsentPass FirstSeenPass, string FirstSeenUrl, DateTimeOffset? Expires)`
  - `sealed record CatalogueEntry(string Pattern, LocalisedText Provider, string Category, LocalisedText Purpose, int? DurationDays, bool Tracker, bool Expected)`
  - `sealed class CookieCatalogue` with `static CookieCatalogue Default()`, `static CookieCatalogue Parse(string json)`, `string UnknownCategory`, `IReadOnlyList<CatalogueEntry> Entries`, `CatalogueEntry? Match(string name)`, `IReadOnlyList<CatalogueEntry> Expected`
- Tasks 3, 4 and 5 depend on all of these.

- [ ] **Step 1: Write the small value types**

`NDSTK.CookieScan.Core/Locale.cs`:

```csharp
using System.Text.Json.Serialization;

namespace NDSTK.CookieScan.Core;

/// <summary>The languages the scanner can write visitor-facing copy in.</summary>
public enum Locale
{
    Sv,
    En,
}

/// <summary>
/// A string in both shipped languages.
/// </summary>
/// <remarks>
/// Catalogue text ends up on a public policy page as legal wording, so it cannot be one language
/// with the other generated at runtime: "Denna webbplats" is not a translation job the scanner
/// should be doing. Both are written down and the locale picks one.
/// </remarks>
public sealed record LocalisedText(
    [property: JsonPropertyName("sv")] string Sv,
    [property: JsonPropertyName("en")] string En)
{
    public string For(Locale locale) => locale == Locale.Sv ? Sv : En;
}
```

`NDSTK.CookieScan.Core/StorageKind.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>Where a scanned entry was stored in the browser.</summary>
public enum StorageKind
{
    Cookie,
    LocalStorage,
    SessionStorage,
}

/// <summary>
/// Wire names for <see cref="StorageKind"/>, matching the CookieBanner package's "Storage type"
/// dropdown exactly.
/// </summary>
/// <remarks>
/// The dropdown offers <c>Cookie</c>, <c>localStorage</c>, <c>sessionStorage</c> and <c>Pixel</c> -
/// mixed case, and not derivable from the enum member names. Kept as an explicit map so renaming a
/// member here cannot silently write a value the dropdown will not accept. The scanner never emits
/// <c>Pixel</c>; see the spec's non-goals.
/// </remarks>
public static class StorageKinds
{
    public static string ToWireName(StorageKind kind) => kind switch
    {
        StorageKind.Cookie => "Cookie",
        StorageKind.LocalStorage => "localStorage",
        StorageKind.SessionStorage => "sessionStorage",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
```

`NDSTK.CookieScan.Core/ConsentPass.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>
/// The consent state a scan pass ran under. Declared in the order the passes run, because the
/// earliest pass an entry appeared in is what decides its category.
/// </summary>
public enum ConsentPass
{
    Undecided = 0,
    RejectAll = 1,
    Preferences = 2,
    Statistics = 3,
    Marketing = 4,
    AcceptAll = 5,

    /// <summary>
    /// The signed-in dimension. Deliberately outside the comparable sequence: it visits a
    /// different URL set, so its findings cannot be compared by pass order against the six.
    /// </summary>
    MemberArea = 6,
}

/// <summary>
/// What each pass granted, and what an entry first appearing in it therefore implies.
/// </summary>
public static class ConsentPasses
{
    /// <summary>The six passes that share one URL list and are therefore comparable by order.</summary>
    public static readonly IReadOnlyList<ConsentPass> Comparable =
    [
        ConsentPass.Undecided,
        ConsentPass.RejectAll,
        ConsentPass.Preferences,
        ConsentPass.Statistics,
        ConsentPass.Marketing,
        ConsentPass.AcceptAll,
    ];

    /// <summary>
    /// The categories granted during a pass. The violation rule compares a cookie's catalogued
    /// category against this set: a statistics cookie appearing while only preferences was granted
    /// is a violation just as plainly as one appearing after a flat refusal.
    /// </summary>
    public static IReadOnlySet<string> Granted(ConsentPass pass) => pass switch
    {
        ConsentPass.Undecided => Set(),
        ConsentPass.RejectAll => Set(),
        ConsentPass.Preferences => Set("preferences"),
        ConsentPass.Statistics => Set("statistics"),
        ConsentPass.Marketing => Set("marketing"),
        ConsentPass.AcceptAll => Set("preferences", "statistics", "marketing"),
        ConsentPass.MemberArea => Set("preferences", "statistics", "marketing"),
        _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, null),
    };

    /// <summary>
    /// The category implied by an entry first appearing in this pass, or <c>null</c> when the pass
    /// implies nothing. Only <see cref="ConsentPass.AcceptAll"/> returns null: it grants
    /// everything, so an entry first seen there could belong to any of the three.
    /// </summary>
    public static string? ImpliedCategory(ConsentPass pass) => pass switch
    {
        ConsentPass.Undecided => "necessary",
        ConsentPass.RejectAll => "necessary",
        ConsentPass.Preferences => "preferences",
        ConsentPass.Statistics => "statistics",
        ConsentPass.Marketing => "marketing",
        ConsentPass.AcceptAll => null,

        // A cookie that only exists once you are signed in is a session cookie by construction.
        ConsentPass.MemberArea => "necessary",
        _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, null),
    };

    private static IReadOnlySet<string> Set(params string[] categories)
        => new HashSet<string>(categories, StringComparer.Ordinal);
}
```

`NDSTK.CookieScan.Core/ObservedEntry.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>
/// One cookie or storage key a scan actually saw, reduced to what the rules need.
/// </summary>
/// <remarks>
/// Free of Playwright types on purpose: that is what lets category inference be unit tested
/// without launching a browser.
/// <paramref name="Expires"/> is null for a session cookie and for every storage entry.
/// </remarks>
public sealed record ObservedEntry(
    string Name,
    StorageKind Storage,
    ConsentPass FirstSeenPass,
    string FirstSeenUrl,
    DateTimeOffset? Expires);
```

- [ ] **Step 2: Write the failing catalogue tests**

`NDSTK.Tests/CookieCatalogueTests.cs`:

```csharp
using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class CookieCatalogueTests
{
    private const string Json = """
    {
      "unknownCategory": "marketing",
      "entries": [
        { "pattern": "*", "provider": { "sv": "Okänd", "en": "Unknown" },
          "category": "marketing",
          "purpose": { "sv": "Okänt syfte.", "en": "Unknown purpose." } },
        { "pattern": "_ga*", "provider": { "sv": "Google", "en": "Google" },
          "category": "statistics", "tracker": true,
          "purpose": { "sv": "Bred.", "en": "Broad." } },
        { "pattern": "_ga_*", "provider": { "sv": "Google Analytics", "en": "Google Analytics" },
          "category": "statistics", "tracker": true, "durationDays": 730,
          "purpose": { "sv": "Mäter.", "en": "Measures." } },
        { "pattern": "UMB_MEMBER", "provider": { "sv": "Umbraco", "en": "Umbraco" },
          "category": "necessary", "expected": true, "durationDays": 0,
          "purpose": { "sv": "Inloggning.", "en": "Login." } }
      ]
    }
    """;

    private static CookieCatalogue Catalogue() => CookieCatalogue.Parse(Json);

    // Three patterns match "_ga_ABC123". The most specific has to win, or every Google Analytics
    // property cookie is declared with the catch-all's wording and the wrong category.
    [Fact]
    public void The_most_specific_matching_pattern_wins()
    {
        CatalogueEntry? match = Catalogue().Match("_ga_ABC123");

        Assert.NotNull(match);
        Assert.Equal("_ga_*", match.Pattern);
        Assert.Equal("Google Analytics", match.Provider.Sv);
    }

    [Fact]
    public void A_looser_pattern_still_wins_when_it_is_the_only_one_that_fits()
    {
        CatalogueEntry? match = Catalogue().Match("_gat");

        Assert.NotNull(match);
        Assert.Equal("_ga*", match.Pattern);
    }

    [Fact]
    public void An_exact_pattern_wins_over_the_catch_all()
    {
        CatalogueEntry? match = Catalogue().Match("UMB_MEMBER");

        Assert.NotNull(match);
        Assert.Equal("UMB_MEMBER", match.Pattern);
        Assert.Equal("necessary", match.Category);
        Assert.False(match.Tracker);
    }

    [Fact]
    public void An_unmatched_name_falls_through_to_the_catch_all_when_one_exists()
    {
        CatalogueEntry? match = Catalogue().Match("totally_unknown_thing");

        Assert.NotNull(match);
        Assert.Equal("*", match.Pattern);
    }

    // A catalogue with no catch-all must return null rather than inventing a match: "unknown" is
    // what routes a cookie into the needs-review path instead of a confident wrong declaration.
    // The shipped catalogue deliberately has no catch-all, so this is the real code path.
    [Fact]
    public void A_catalogue_without_a_catch_all_returns_null_for_an_unknown_name()
    {
        CookieCatalogue catalogue = CookieCatalogue.Parse("""
        { "unknownCategory": "marketing", "entries": [
          { "pattern": "UMB_MEMBER", "provider": { "sv": "U", "en": "U" },
            "category": "necessary", "purpose": { "sv": "S", "en": "S" } } ] }
        """);

        Assert.Null(catalogue.Match("_fbp"));
    }

    // The report's "expected but not observed" section has nothing to draw on without this flag,
    // and it must exclude third-party entries: an absent Google cookie is normal, an absent
    // UMB_MEMBER on a site with a login is a finding.
    [Fact]
    public void Expected_selects_only_the_flagged_entries()
    {
        IReadOnlyList<CatalogueEntry> expected = Catalogue().Expected;

        Assert.Single(expected);
        Assert.Equal("UMB_MEMBER", expected[0].Pattern);
    }

    [Fact]
    public void Duration_days_is_read_when_present_and_null_when_absent()
    {
        Assert.Equal(730, Catalogue().Match("_ga_ABC")!.DurationDays);
        Assert.Equal(0, Catalogue().Match("UMB_MEMBER")!.DurationDays);
        Assert.Null(Catalogue().Match("_gat")!.DurationDays);
    }

    [Fact]
    public void Localised_text_resolves_per_locale()
    {
        CatalogueEntry entry = Catalogue().Match("_ga_ABC")!;

        Assert.Equal("Mäter.", entry.Purpose.For(Locale.Sv));
        Assert.Equal("Measures.", entry.Purpose.For(Locale.En));
    }

    [Fact]
    public void Unknown_category_is_read_from_the_document()
    {
        Assert.Equal("marketing", Catalogue().UnknownCategory);
    }

    // The embedded catalogue is what a fresh exe uses, so a typo in it is a shipping bug no other
    // test would catch.
    [Fact]
    public void The_embedded_default_catalogue_parses_and_knows_this_sites_stack()
    {
        CookieCatalogue catalogue = CookieCatalogue.Default();

        Assert.NotEmpty(catalogue.Entries);
        Assert.Equal("necessary", catalogue.Match("UMB_MEMBER")!.Category);
        Assert.Equal("necessary", catalogue.Match(".AspNetCore.Antiforgery.CfDJ8x")!.Category);
        Assert.Equal("necessary", catalogue.Match(".AspNetCore.Mvc.CookieTempDataProvider")!.Category);
        Assert.Equal("statistics", catalogue.Match("_ga_ABC123")!.Category);
        Assert.True(catalogue.Match("_ga_ABC123")!.Tracker);
    }

    // The shipped catalogue must have no catch-all, or nothing can ever reach needs-review.
    [Fact]
    public void The_embedded_catalogue_has_no_catch_all()
    {
        Assert.Null(CookieCatalogue.Default().Match("some_cookie_nobody_has_heard_of"));
    }

    // The TempData cookie is the one gap already known from reading the code, so it has to be
    // flagged expected or the report can never tell anyone it is missing.
    [Fact]
    public void The_embedded_catalogue_expects_the_temp_data_cookie()
    {
        Assert.Contains(
            CookieCatalogue.Default().Expected,
            entry => entry.Pattern == ".AspNetCore.Mvc.CookieTempDataProvider");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter CookieCatalogueTests`
Expected: build failure, `CS0246: The type or namespace name 'CookieCatalogue' could not be found`.

- [ ] **Step 4: Write `CatalogueEntry`**

`NDSTK.CookieScan.Core/CatalogueEntry.cs`:

```csharp
using System.Text.Json.Serialization;

namespace NDSTK.CookieScan.Core;

/// <summary>
/// One row of the cookie catalogue: what a recognised name is, who sets it, and what to write
/// about it on the policy page.
/// </summary>
/// <remarks>
/// <paramref name="DurationDays"/> is machine-readable rather than pre-written text so that
/// <see cref="DurationFormatter"/> can render it in the requested locale - the spec's original
/// "24 månader" string could not honour an English run. <c>0</c> means a session cookie;
/// <c>null</c> means no documented lifetime, so use what the browser reported.
/// </remarks>
public sealed record CatalogueEntry(
    [property: JsonPropertyName("pattern")] string Pattern,
    [property: JsonPropertyName("provider")] LocalisedText Provider,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("purpose")] LocalisedText Purpose,
    [property: JsonPropertyName("durationDays")] int? DurationDays = null,
    [property: JsonPropertyName("tracker")] bool Tracker = false,
    [property: JsonPropertyName("expected")] bool Expected = false);
```

- [ ] **Step 5: Write `CookieCatalogue`**

`NDSTK.CookieScan.Core/CookieCatalogue.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NDSTK.CookieScan.Core;

/// <summary>
/// The known-cookie catalogue: name patterns mapped to a provider, a category and the wording to
/// put on the policy page.
/// </summary>
/// <remarks>
/// Data rather than code because its <c>purpose</c> text becomes public legal wording, and that
/// must be editable without a rebuild. The embedded copy is the default; a
/// <c>cookie-catalogue.json</c> beside the exe replaces it wholesale.
/// </remarks>
public sealed class CookieCatalogue
{
    private const string EmbeddedName = "NDSTK.CookieScan.Core.Resources.cookie-catalogue.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private CookieCatalogue(string unknownCategory, IReadOnlyList<CatalogueEntry> entries)
    {
        UnknownCategory = unknownCategory;
        Entries = entries;
        Expected = entries.Where(entry => entry.Expected).ToArray();
    }

    /// <summary>Category given to an unrecognised name that no pass could attribute.</summary>
    public string UnknownCategory { get; }

    public IReadOnlyList<CatalogueEntry> Entries { get; }

    /// <summary>
    /// Entries known to apply to this site's own stack, so their absence from a scan is itself
    /// worth reporting. Third-party entries are excluded: an absent Google cookie is normal.
    /// </summary>
    public IReadOnlyList<CatalogueEntry> Expected { get; }

    /// <summary>The catalogue compiled into the assembly.</summary>
    public static CookieCatalogue Default()
    {
        using Stream stream = typeof(CookieCatalogue).Assembly
            .GetManifestResourceStream(EmbeddedName)
            ?? throw new InvalidOperationException(
                $"The embedded catalogue '{EmbeddedName}' is missing. Check that "
                + "Resources\\cookie-catalogue.json is still an EmbeddedResource in the csproj.");

        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    public static CookieCatalogue Parse(string json)
    {
        Document? document = JsonSerializer.Deserialize<Document>(json, SerializerOptions)
            ?? throw new InvalidOperationException("The cookie catalogue is empty or not valid JSON.");

        return new CookieCatalogue(
            string.IsNullOrWhiteSpace(document.UnknownCategory) ? "marketing" : document.UnknownCategory,
            document.Entries ?? []);
    }

    /// <summary>
    /// The best matching entry for <paramref name="name"/>, or null when nothing matches.
    /// </summary>
    /// <remarks>
    /// Most specific wins: fewest characters absorbed by wildcards, then the longer literal
    /// prefix. Returning null rather than a guess is what routes an unrecognised cookie into the
    /// needs-review path instead of a confident-looking wrong declaration.
    /// </remarks>
    public CatalogueEntry? Match(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return Entries
            .Where(entry => CookieNameMatcher.Matches(entry.Pattern, name))
            .OrderBy(entry => CookieNameMatcher.WildcardCharCount(entry.Pattern, name))
            .ThenByDescending(entry => CookieNameMatcher.LiteralPrefixLength(entry.Pattern))
            .FirstOrDefault();
    }

    private sealed record Document(
        [property: JsonPropertyName("unknownCategory")] string? UnknownCategory,
        [property: JsonPropertyName("entries")] IReadOnlyList<CatalogueEntry>? Entries);
}
```

- [ ] **Step 6: Write the real embedded catalogue**

Replace `NDSTK.CookieScan.Core/Resources/cookie-catalogue.json` entirely. There is deliberately **no `"*"` catch-all entry**: an unrecognised name must reach the needs-review path rather than being handed a catch-all's wording, which would read as a real declaration on a public page.

```json
{
  "unknownCategory": "marketing",
  "entries": [
    {
      "pattern": "ndstk-consent",
      "provider": { "sv": "Denna webbplats", "en": "This website" },
      "category": "necessary",
      "expected": true,
      "durationDays": 365,
      "purpose": {
        "sv": "Sparar dina cookieval så att vi inte behöver fråga igen.",
        "en": "Stores your cookie choices so we do not have to ask again."
      }
    },
    {
      "pattern": ".AspNetCore.Antiforgery.*",
      "provider": { "sv": "Denna webbplats", "en": "This website" },
      "category": "necessary",
      "expected": true,
      "durationDays": 0,
      "purpose": {
        "sv": "Skyddar formulär mot förfalskade anrop från andra webbplatser.",
        "en": "Protects forms against cross-site request forgery."
      }
    },
    {
      "pattern": ".AspNetCore.Mvc.CookieTempDataProvider",
      "provider": { "sv": "Denna webbplats", "en": "This website" },
      "category": "necessary",
      "expected": true,
      "durationDays": 0,
      "purpose": {
        "sv": "Bär med sig ett meddelande, till exempel att en bokning gick igenom, till nästa sidvisning.",
        "en": "Carries a message, such as a completed booking, to the next page view."
      }
    },
    {
      "pattern": ".AspNetCore.Culture",
      "provider": { "sv": "Denna webbplats", "en": "This website" },
      "category": "necessary",
      "purpose": {
        "sv": "Kommer ihåg vilket språk du har valt.",
        "en": "Remembers the language you chose."
      }
    },
    {
      "pattern": "UMB_MEMBER",
      "provider": { "sv": "Umbraco", "en": "Umbraco" },
      "category": "necessary",
      "expected": true,
      "durationDays": 0,
      "purpose": {
        "sv": "Håller dig inloggad som medlem.",
        "en": "Keeps a signed-in member logged in."
      }
    },
    {
      "pattern": "ASP.NET_SessionId",
      "provider": { "sv": "Denna webbplats", "en": "This website" },
      "category": "necessary",
      "durationDays": 0,
      "purpose": {
        "sv": "Kopplar dina anrop till samma session på servern.",
        "en": "Ties your requests to the same session on the server."
      }
    },
    {
      "pattern": "_ga",
      "provider": { "sv": "Google Analytics", "en": "Google Analytics" },
      "category": "statistics",
      "tracker": true,
      "durationDays": 730,
      "purpose": {
        "sv": "Skiljer besökare åt för att mäta hur webbplatsen används.",
        "en": "Distinguishes visitors in order to measure how the site is used."
      }
    },
    {
      "pattern": "_ga_*",
      "provider": { "sv": "Google Analytics", "en": "Google Analytics" },
      "category": "statistics",
      "tracker": true,
      "durationDays": 730,
      "purpose": {
        "sv": "Håller reda på sessionen för en enskild Google Analytics-egenskap.",
        "en": "Keeps session state for one Google Analytics property."
      }
    },
    {
      "pattern": "_gid",
      "provider": { "sv": "Google Analytics", "en": "Google Analytics" },
      "category": "statistics",
      "tracker": true,
      "durationDays": 1,
      "purpose": {
        "sv": "Skiljer besökare åt under ett dygn.",
        "en": "Distinguishes visitors within a single day."
      }
    },
    {
      "pattern": "_gcl_au",
      "provider": { "sv": "Google Ads", "en": "Google Ads" },
      "category": "marketing",
      "tracker": true,
      "durationDays": 90,
      "purpose": {
        "sv": "Mäter om ett besök kom från en annons.",
        "en": "Measures whether a visit came from an advert."
      }
    },
    {
      "pattern": "_fbp",
      "provider": { "sv": "Meta", "en": "Meta" },
      "category": "marketing",
      "tracker": true,
      "durationDays": 90,
      "purpose": {
        "sv": "Kopplar ditt besök till annonser på Facebook och Instagram.",
        "en": "Links your visit to adverts on Facebook and Instagram."
      }
    },
    {
      "pattern": "_hj*",
      "provider": { "sv": "Hotjar", "en": "Hotjar" },
      "category": "statistics",
      "tracker": true,
      "durationDays": 365,
      "purpose": {
        "sv": "Spelar in hur besökare rör sig på sidan.",
        "en": "Records how visitors move around the page."
      }
    },
    {
      "pattern": "VISITOR_INFO1_LIVE",
      "provider": { "sv": "YouTube", "en": "YouTube" },
      "category": "marketing",
      "tracker": true,
      "durationDays": 180,
      "purpose": {
        "sv": "Uppskattar din bandbredd och kommer ihåg inställningar för inbäddade videor.",
        "en": "Estimates your bandwidth and remembers settings for embedded video."
      }
    },
    {
      "pattern": "YSC",
      "provider": { "sv": "YouTube", "en": "YouTube" },
      "category": "marketing",
      "tracker": true,
      "durationDays": 0,
      "purpose": {
        "sv": "Räknar visningar av inbäddade videor.",
        "en": "Counts views of embedded video."
      }
    },
    {
      "pattern": "vuid",
      "provider": { "sv": "Vimeo", "en": "Vimeo" },
      "category": "statistics",
      "tracker": true,
      "durationDays": 730,
      "purpose": {
        "sv": "Kommer ihåg hur långt du har sett av en inbäddad video.",
        "en": "Remembers how far you watched an embedded video."
      }
    }
  ]
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter CookieCatalogueTests`
Expected: every test in the class passes.

- [ ] **Step 8: Verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded, 0 warnings.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all tests pass, including Task 1's.
Run: `git status --short` — expected: the eight files of this task and nothing else.

---

## Task 3: `DurationFormatter` and `Wording`

**Files:**
- Create: `NDSTK.CookieScan.Core/DurationFormatter.cs`
- Create: `NDSTK.CookieScan.Core/Wording.cs`
- Test: `NDSTK.Tests/DurationFormatterTests.cs`

**Interfaces:**
- Consumes: `StorageKind`, `Locale` from Task 2.
- Produces:
  - `static class DurationFormatter` with `static string Format(StorageKind storage, int? durationDays, DateTimeOffset? expires, DateTimeOffset now, Locale locale)`
  - `static class Wording` with `static string UnknownProvider(Locale)`, `static string UnknownPurpose(Locale)`, `static string NeedsReviewPurpose(Locale)`
- Task 4 depends on both.

- [ ] **Step 1: Write the failing tests**

`NDSTK.Tests/DurationFormatterTests.cs`:

```csharp
using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class DurationFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static string Format(
        int? durationDays = null,
        DateTimeOffset? expires = null,
        StorageKind storage = StorageKind.Cookie,
        Locale locale = Locale.Sv)
        => DurationFormatter.Format(storage, durationDays, expires, Now, locale);

    // A cookie with no expiry dies with the browser session. So does one whose expiry has already
    // passed - a scan that catches a cookie mid-deletion must not declare it as lasting -3 days.
    [Fact]
    public void No_expiry_is_a_session_cookie()
    {
        Assert.Equal("Session", Format());
    }

    [Fact]
    public void An_expiry_in_the_past_is_a_session_cookie()
    {
        Assert.Equal("Session", Format(expires: Now.AddDays(-3)));
    }

    [Fact]
    public void A_catalogue_duration_of_zero_days_is_a_session_cookie()
    {
        Assert.Equal("Session", Format(durationDays: 0));
    }

    // localStorage has no expiry at all, and calling that "Session" would be a lie in the
    // visitor's favour - it survives closing the browser. That distinction is the whole reason
    // the policy page records a storage type.
    [Fact]
    public void Local_storage_lasts_until_it_is_deleted()
    {
        Assert.Equal("Tills den raderas", Format(storage: StorageKind.LocalStorage));
        Assert.Equal("Until deleted", Format(storage: StorageKind.LocalStorage, locale: Locale.En));
    }

    [Fact]
    public void Session_storage_is_a_session()
    {
        Assert.Equal("Session", Format(storage: StorageKind.SessionStorage));
    }

    [Fact]
    public void Under_a_day_reads_in_hours()
    {
        Assert.Equal("2 timmar", Format(expires: Now.AddHours(2)));
        Assert.Equal("2 hours", Format(expires: Now.AddHours(2), locale: Locale.En));
    }

    // Visitors read this text on a public page, so "1 timmar" is not acceptable output.
    [Fact]
    public void Singular_and_plural_forms_differ_in_both_locales()
    {
        Assert.Equal("1 timme", Format(expires: Now.AddHours(1)));
        Assert.Equal("1 hour", Format(expires: Now.AddHours(1), locale: Locale.En));
        Assert.Equal("1 dag", Format(durationDays: 1));
        Assert.Equal("1 day", Format(durationDays: 1, locale: Locale.En));
    }

    // No month singular is asserted above because none is reachable, and that is worth pinning
    // down rather than leaving as a surprise: the smallest value that reaches the months branch is
    // 60 days, which is 1.97 months and rounds to 2. Anything shorter renders in days by design.
    // The singular arm of the switch stays as defensive code. Lower MonthsFromDays to 45 if a
    // "1 månad" output is ever wanted.
    [Fact]
    public void The_smallest_month_output_is_two_because_of_the_sixty_day_threshold()
    {
        Assert.Equal("59 dagar", Format(durationDays: 59));
        Assert.Equal("2 månader", Format(durationDays: 60));
    }

    // Never "0 timmar". A cookie that expires in forty seconds still exists, and rounding it away
    // to zero would read as a mistake rather than as a very short lifetime.
    [Fact]
    public void A_sub_minute_expiry_floors_to_one_hour_rather_than_zero()
    {
        Assert.Equal("1 timme", Format(expires: Now.AddSeconds(40)));
    }

    [Fact]
    public void Between_one_day_and_sixty_reads_in_days()
    {
        Assert.Equal("30 dagar", Format(expires: Now.AddDays(30)));
        Assert.Equal("30 days", Format(expires: Now.AddDays(30), locale: Locale.En));
    }

    // 30.44 days per month, not 30, so a year does not come out as "12 månader och lite".
    [Fact]
    public void A_year_reads_as_twelve_months()
    {
        Assert.Equal("12 månader", Format(durationDays: 365));
        Assert.Equal("12 months", Format(durationDays: 365, locale: Locale.En));
    }

    [Fact]
    public void Two_years_reads_as_twenty_four_months()
    {
        Assert.Equal("24 månader", Format(durationDays: 730));
    }

    // The catalogue's documented lifetime beats whatever this one browser happened to report,
    // which may be truncated by the browser's own cap on cookie lifetimes.
    [Fact]
    public void A_catalogue_duration_overrides_the_observed_expiry()
    {
        Assert.Equal("24 månader", Format(durationDays: 730, expires: Now.AddDays(7)));
    }

    [Fact]
    public void Wording_differs_between_an_unknown_and_a_needs_review_cookie()
    {
        Assert.NotEqual(Wording.UnknownPurpose(Locale.Sv), Wording.NeedsReviewPurpose(Locale.Sv));
        Assert.NotEmpty(Wording.UnknownProvider(Locale.Sv));
        Assert.NotEmpty(Wording.UnknownProvider(Locale.En));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter DurationFormatterTests`
Expected: build failure, `CS0103: The name 'DurationFormatter' does not exist`.

- [ ] **Step 3: Write `Wording`**

`NDSTK.CookieScan.Core/Wording.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>
/// Copy the scanner writes itself, for a cookie the catalogue does not recognise.
/// </summary>
/// <remarks>
/// This text lands on a public policy page, so it says plainly that a human has not checked it
/// yet. Inventing a plausible purpose would be worse than admitting there isn't one: a visitor
/// reading a confident sentence has no way to know it was guessed.
/// </remarks>
public static class Wording
{
    public static string UnknownProvider(Locale locale)
        => locale == Locale.Sv ? "Okänd" : "Unknown";

    /// <summary>For a cookie whose category a pass established but whose purpose is unknown.</summary>
    public static string UnknownPurpose(Locale locale)
        => locale == Locale.Sv
            ? "Hittad av cookieskannern. Syftet är inte fastställt än."
            : "Found by the cookie scanner. Its purpose has not been established yet.";

    /// <summary>For a cookie no pass could attribute, so neither purpose nor category is settled.</summary>
    public static string NeedsReviewPurpose(Locale locale)
        => locale == Locale.Sv
            ? "Hittad av cookieskannern. Både syfte och kategori behöver kontrolleras."
            : "Found by the cookie scanner. Both its purpose and its category need checking.";
}
```

- [ ] **Step 4: Write `DurationFormatter`**

`NDSTK.CookieScan.Core/DurationFormatter.cs`:

```csharp
using System.Globalization;

namespace NDSTK.CookieScan.Core;

/// <summary>
/// Turns a documented lifetime or an observed expiry into the sentence a visitor reads in the
/// duration column of the cookie policy table.
/// </summary>
public static class DurationFormatter
{
    // Mean days per month. 30 would render a 365-day cookie as 12.17 months and a 730-day one as
    // 24.3, so the two commonest real lifetimes would both round wrong.
    private const double DaysPerMonth = 30.44;

    // Below this, days read better than months: "45 dagar" is clearer than "1 månad".
    private const int MonthsFromDays = 60;

    /// <summary>
    /// The duration text. <paramref name="durationDays"/> is the catalogue's documented lifetime
    /// and wins when present - a browser may cap or truncate what it reports.
    /// <c>0</c> means a session cookie.
    /// </summary>
    public static string Format(
        StorageKind storage,
        int? durationDays,
        DateTimeOffset? expires,
        DateTimeOffset now,
        Locale locale)
    {
        // Storage kind decides before any lifetime does: neither of these has an expiry to read,
        // and localStorage outliving the session is the fact worth telling a visitor.
        if (storage == StorageKind.LocalStorage)
        {
            return locale == Locale.Sv ? "Tills den raderas" : "Until deleted";
        }

        if (storage == StorageKind.SessionStorage)
        {
            return Session();
        }

        if (durationDays is int documented)
        {
            return documented <= 0 ? Session() : FromDays(documented, locale);
        }

        if (expires is null || expires <= now)
        {
            return Session();
        }

        TimeSpan span = expires.Value - now;

        if (span.TotalHours < 24)
        {
            return Plural(AtLeastOne(span.TotalHours), Unit.Hour, locale);
        }

        return FromDays(AtLeastOne(span.TotalDays), locale);

        string Session() => "Session";
    }

    private static string FromDays(int days, Locale locale)
        => days < MonthsFromDays
            ? Plural(days, Unit.Day, locale)
            : Plural(AtLeastOne(days / DaysPerMonth), Unit.Month, locale);

    // Rounds to the nearest whole unit but never to zero: a cookie expiring in forty seconds
    // still exists, and "0 timmar" reads as a bug rather than as a very short lifetime.
    private static int AtLeastOne(double value) => Math.Max(1, (int)Math.Round(value));

    private static string Plural(int count, Unit unit, Locale locale)
    {
        string word = (unit, locale, count) switch
        {
            (Unit.Hour, Locale.Sv, 1) => "timme",
            (Unit.Hour, Locale.Sv, _) => "timmar",
            (Unit.Hour, _, 1) => "hour",
            (Unit.Hour, _, _) => "hours",
            (Unit.Day, Locale.Sv, 1) => "dag",
            (Unit.Day, Locale.Sv, _) => "dagar",
            (Unit.Day, _, 1) => "day",
            (Unit.Day, _, _) => "days",
            (Unit.Month, Locale.Sv, 1) => "månad",
            (Unit.Month, Locale.Sv, _) => "månader",
            (Unit.Month, _, 1) => "month",
            _ => "months",
        };

        return string.Create(CultureInfo.InvariantCulture, $"{count} {word}");
    }

    private enum Unit
    {
        Hour,
        Day,
        Month,
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter DurationFormatterTests`
Expected: every test in the class passes.

- [ ] **Step 6: Verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded, 0 warnings.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all tests pass.
Run: `git status --short` — expected: the three files of this task and nothing else.

---

## Task 4: `CategoryInference`

**Files:**
- Create: `NDSTK.CookieScan.Core/CookieDeclarationCandidate.cs`
- Create: `NDSTK.CookieScan.Core/CategoryInference.cs`
- Test: `NDSTK.Tests/CategoryInferenceTests.cs`

**Interfaces:**
- Consumes: `ObservedEntry`, `CookieCatalogue`, `CatalogueEntry`, `ConsentPass`, `ConsentPasses`, `StorageKind`, `StorageKinds`, `Locale` from Task 2; `DurationFormatter`, `Wording` from Task 3.
- Produces:
  - `enum CandidateFlag { None, Violation, NeedsReview }`
  - `sealed record CookieDeclarationCandidate(string Name, string Provider, string Category, string Purpose, string Duration, string StorageType, CandidateFlag Flag, ConsentPass FirstSeenPass, string FirstSeenUrl)`
  - `static class CategoryInference` with `static CookieDeclarationCandidate Classify(ObservedEntry entry, CookieCatalogue catalogue, DateTimeOffset now, Locale locale)`
- Task 5 consumes `CookieDeclarationCandidate` and `CandidateFlag`; Tasks 10, 11 and 12 consume the record's shape.

- [ ] **Step 1: Write the failing tests**

`NDSTK.Tests/CategoryInferenceTests.cs`:

```csharp
using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class CategoryInferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly CookieCatalogue Catalogue = CookieCatalogue.Parse("""
    {
      "unknownCategory": "marketing",
      "entries": [
        { "pattern": "UMB_MEMBER", "provider": { "sv": "Umbraco", "en": "Umbraco" },
          "category": "necessary", "durationDays": 0,
          "purpose": { "sv": "Inloggning.", "en": "Login." } },
        { "pattern": "_ga_*", "provider": { "sv": "Google Analytics", "en": "Google Analytics" },
          "category": "statistics", "tracker": true, "durationDays": 730,
          "purpose": { "sv": "Mäter.", "en": "Measures." } },
        { "pattern": "_fbp", "provider": { "sv": "Meta", "en": "Meta" },
          "category": "marketing", "tracker": true, "durationDays": 90,
          "purpose": { "sv": "Annonser.", "en": "Adverts." } }
      ]
    }
    """);

    private static CookieDeclarationCandidate Classify(
        string name,
        ConsentPass pass,
        StorageKind storage = StorageKind.Cookie,
        DateTimeOffset? expires = null)
        => CategoryInference.Classify(
            new ObservedEntry(name, storage, pass, "https://ndstk.se/", expires),
            Catalogue,
            Now,
            Locale.Sv);

    // Nothing has been consented to in the first two passes, so anything set there is either
    // strictly necessary or a violation. An unrecognised cookie gets the benefit of the doubt on
    // category - the pass genuinely establishes it - but not on purpose.
    [Fact]
    public void An_unknown_cookie_in_the_undecided_pass_is_necessary()
    {
        CookieDeclarationCandidate candidate = Classify("mystery", ConsentPass.Undecided);

        Assert.Equal("necessary", candidate.Category);
        Assert.Equal(CandidateFlag.None, candidate.Flag);
    }

    [Theory]
    [InlineData(ConsentPass.Preferences, "preferences")]
    [InlineData(ConsentPass.Statistics, "statistics")]
    [InlineData(ConsentPass.Marketing, "marketing")]
    public void The_pass_that_first_shows_an_unknown_cookie_names_its_category(
        ConsentPass pass, string expected)
    {
        Assert.Equal(expected, Classify("mystery", pass).Category);
    }

    // This is the finding the whole design exists for: a tracker set despite a refusal.
    [Fact]
    public void A_tracker_in_the_reject_all_pass_is_a_violation()
    {
        CookieDeclarationCandidate candidate = Classify("_ga_ABC123", ConsentPass.RejectAll);

        Assert.Equal(CandidateFlag.Violation, candidate.Flag);
        Assert.Equal("statistics", candidate.Category);
    }

    [Fact]
    public void A_tracker_before_any_choice_exists_is_a_violation()
    {
        Assert.Equal(CandidateFlag.Violation, Classify("_fbp", ConsentPass.Undecided).Flag);
    }

    // The case a pass-1-and-2-only rule would have waved through: statistics was never granted in
    // the preferences pass, so a statistics cookie appearing there violates consent just as
    // plainly as one appearing after a flat refusal.
    [Fact]
    public void A_statistics_cookie_in_the_preferences_pass_is_a_violation()
    {
        Assert.Equal(CandidateFlag.Violation, Classify("_ga_ABC123", ConsentPass.Preferences).Flag);
    }

    [Fact]
    public void A_tracker_in_the_pass_that_granted_its_own_category_is_not_a_violation()
    {
        Assert.Equal(CandidateFlag.None, Classify("_ga_ABC123", ConsentPass.Statistics).Flag);
        Assert.Equal(CandidateFlag.None, Classify("_fbp", ConsentPass.Marketing).Flag);
    }

    [Fact]
    public void A_necessary_cookie_is_never_a_violation_in_any_pass()
    {
        foreach (ConsentPass pass in ConsentPasses.Comparable)
        {
            Assert.Equal(CandidateFlag.None, Classify("UMB_MEMBER", pass).Flag);
        }
    }

    // Accept-all grants everything, so nothing appearing there can be a violation - and an
    // unrecognised name there cannot be attributed to one category either.
    [Fact]
    public void An_unknown_cookie_first_seen_under_accept_all_needs_review()
    {
        CookieDeclarationCandidate candidate = Classify("mystery", ConsentPass.AcceptAll);

        Assert.Equal(CandidateFlag.NeedsReview, candidate.Flag);
        Assert.Equal("marketing", candidate.Category);
        Assert.Equal(Wording.NeedsReviewPurpose(Locale.Sv), candidate.Purpose);
    }

    [Fact]
    public void A_known_cookie_under_accept_all_is_neither_a_violation_nor_needs_review()
    {
        Assert.Equal(CandidateFlag.None, Classify("_ga_ABC123", ConsentPass.AcceptAll).Flag);
    }

    // A cookie that only exists behind a login is a session cookie by construction.
    [Fact]
    public void An_unknown_cookie_found_only_in_the_member_area_is_necessary()
    {
        CookieDeclarationCandidate candidate = Classify("member_thing", ConsentPass.MemberArea);

        Assert.Equal("necessary", candidate.Category);
        Assert.Equal(CandidateFlag.None, candidate.Flag);
    }

    // Two Google Analytics properties must not become two blocks. Collapsing the name onto the
    // catalogue pattern is what makes the merge idempotent for a whole family of cookies.
    [Fact]
    public void A_recognised_name_collapses_onto_its_catalogue_pattern()
    {
        Assert.Equal("_ga_*", Classify("_ga_ABC123", ConsentPass.Statistics).Name);
        Assert.Equal("_ga_*", Classify("_ga_XYZ789", ConsentPass.Statistics).Name);
    }

    [Fact]
    public void An_unrecognised_name_is_kept_verbatim()
    {
        Assert.Equal("mystery", Classify("mystery", ConsentPass.Undecided).Name);
    }

    [Fact]
    public void A_recognised_cookie_takes_the_catalogues_provider_purpose_and_duration()
    {
        CookieDeclarationCandidate candidate = Classify("_ga_ABC123", ConsentPass.Statistics);

        Assert.Equal("Google Analytics", candidate.Provider);
        Assert.Equal("Mäter.", candidate.Purpose);
        Assert.Equal("24 månader", candidate.Duration);
    }

    [Fact]
    public void An_unrecognised_cookie_takes_generated_wording_and_the_observed_duration()
    {
        CookieDeclarationCandidate candidate =
            Classify("mystery", ConsentPass.Statistics, expires: Now.AddDays(30));

        Assert.Equal(Wording.UnknownProvider(Locale.Sv), candidate.Provider);
        Assert.Equal(Wording.UnknownPurpose(Locale.Sv), candidate.Purpose);
        Assert.Equal("30 dagar", candidate.Duration);
    }

    // The storage type has to survive as one of the package dropdown's exact values, or the
    // endpoint rejects the declaration.
    [Theory]
    [InlineData(StorageKind.Cookie, "Cookie")]
    [InlineData(StorageKind.LocalStorage, "localStorage")]
    [InlineData(StorageKind.SessionStorage, "sessionStorage")]
    public void The_storage_type_is_written_as_the_dropdowns_own_value(
        StorageKind storage, string expected)
    {
        Assert.Equal(expected, Classify("mystery", ConsentPass.Undecided, storage).StorageType);
    }

    [Fact]
    public void The_first_seen_pass_and_url_are_carried_through_for_the_report()
    {
        CookieDeclarationCandidate candidate = Classify("mystery", ConsentPass.Marketing);

        Assert.Equal(ConsentPass.Marketing, candidate.FirstSeenPass);
        Assert.Equal("https://ndstk.se/", candidate.FirstSeenUrl);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter CategoryInferenceTests`
Expected: build failure, `CS0103: The name 'CategoryInference' does not exist`.

- [ ] **Step 3: Write `CookieDeclarationCandidate`**

`NDSTK.CookieScan.Core/CookieDeclarationCandidate.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>How much a candidate needs a human to look at it.</summary>
public enum CandidateFlag
{
    /// <summary>Categorised with evidence. Safe to add.</summary>
    None,

    /// <summary>Set in a pass that had not granted its category. Reported first; fails the run.</summary>
    Violation,

    /// <summary>Only ever seen with everything granted, and unrecognised. Category is a fallback.</summary>
    NeedsReview,
}

/// <summary>
/// A declaration the scan proposes for the policy page, in the exact shape a
/// <c>cookieDefinition</c> block needs.
/// </summary>
/// <remarks>
/// <paramref name="Category"/> and <paramref name="StorageType"/> hold the package's wire values
/// verbatim - lowercase category names, mixed-case storage names - because the merge endpoint
/// validates against those and writes them straight into the block.
/// </remarks>
public sealed record CookieDeclarationCandidate(
    string Name,
    string Provider,
    string Category,
    string Purpose,
    string Duration,
    string StorageType,
    CandidateFlag Flag,
    ConsentPass FirstSeenPass,
    string FirstSeenUrl);
```

- [ ] **Step 4: Write `CategoryInference`**

`NDSTK.CookieScan.Core/CategoryInference.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>
/// Turns one observed entry into a proposed declaration, deciding its category from the consent
/// state it appeared under rather than from a guess at its name.
/// </summary>
public static class CategoryInference
{
    private const string Necessary = "necessary";

    public static CookieDeclarationCandidate Classify(
        ObservedEntry entry,
        CookieCatalogue catalogue,
        DateTimeOffset now,
        Locale locale)
    {
        CatalogueEntry? known = catalogue.Match(entry.Name);

        string category;
        CandidateFlag flag;

        if (known is not null)
        {
            category = known.Category;

            // The rule the whole design exists for. A catalogued category that the pass had not
            // granted means the site set that cookie without permission - whether the visitor
            // refused outright, or granted something else entirely. Necessary is exempt: it is
            // implied rather than granted, so it never appears in a granted set.
            bool granted = category == Necessary
                || ConsentPasses.Granted(entry.FirstSeenPass).Contains(category);

            flag = granted ? CandidateFlag.None : CandidateFlag.Violation;
        }
        else
        {
            string? implied = ConsentPasses.ImpliedCategory(entry.FirstSeenPass);

            // No implied category means accept-all: everything was granted, so the cookie could
            // belong to any of the three and the scan has no evidence for which. The fallback
            // category is a placeholder, which is exactly what NeedsReview announces.
            category = implied ?? catalogue.UnknownCategory;
            flag = implied is null ? CandidateFlag.NeedsReview : CandidateFlag.None;
        }

        return new CookieDeclarationCandidate(
            // Collapsed onto the catalogue pattern, so two Google Analytics properties become one
            // block rather than one per property - and so the next scan recognises them as
            // already declared.
            Name: known?.Pattern ?? entry.Name,
            Provider: known?.Provider.For(locale) ?? Wording.UnknownProvider(locale),
            Category: category,
            Purpose: known?.Purpose.For(locale)
                ?? (flag == CandidateFlag.NeedsReview
                    ? Wording.NeedsReviewPurpose(locale)
                    : Wording.UnknownPurpose(locale)),
            Duration: DurationFormatter.Format(
                entry.Storage, known?.DurationDays, entry.Expires, now, locale),
            StorageType: StorageKinds.ToWireName(entry.Storage),
            Flag: flag,
            FirstSeenPass: entry.FirstSeenPass,
            FirstSeenUrl: entry.FirstSeenUrl);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter CategoryInferenceTests`
Expected: every test in the class passes.

- [ ] **Step 6: Verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded, 0 warnings.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all tests pass.
Run: `git status --short` — expected: the three files of this task and nothing else.

---

## Task 5: `MergePlanner`

**Files:**
- Create: `NDSTK.CookieScan.Core/MergePlan.cs`
- Create: `NDSTK.CookieScan.Core/MergePlanner.cs`
- Test: `NDSTK.Tests/MergePlannerTests.cs`

**Interfaces:**
- Consumes: `CookieDeclarationCandidate`, `CandidateFlag`, `ConsentPass` from Task 4; `CookieCatalogue` from Task 2; `CookieNameMatcher` from Task 1.
- Produces:
  - `sealed record MergePlan(IReadOnlyList<CookieDeclarationCandidate> ToAdd, IReadOnlyList<string> AlreadyDeclared, IReadOnlyList<string> DeclaredButNotFound, IReadOnlyList<string> ExpectedButNotObserved)` with `bool ExceedsCap` and `bool HasWork`
  - `static class MergePlanner` with `const int MaxBlocksPerCall = 50` and `static MergePlan Plan(IEnumerable<CookieDeclarationCandidate> candidates, IEnumerable<string> declaredNames, CookieCatalogue catalogue)`
- Tasks 10, 11 and 12 all consume `MergePlan`.

- [ ] **Step 1: Write the failing tests**

`NDSTK.Tests/MergePlannerTests.cs`:

```csharp
using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class MergePlannerTests
{
    private static readonly CookieCatalogue EmptyCatalogue =
        CookieCatalogue.Parse("""{ "unknownCategory": "marketing", "entries": [] }""");

    private static readonly CookieCatalogue ExpectingUmbMember = CookieCatalogue.Parse("""
    {
      "unknownCategory": "marketing",
      "entries": [
        { "pattern": "UMB_MEMBER", "provider": { "sv": "Umbraco", "en": "Umbraco" },
          "category": "necessary", "expected": true,
          "purpose": { "sv": "Inloggning.", "en": "Login." } },
        { "pattern": "_ga_*", "provider": { "sv": "Google", "en": "Google" },
          "category": "statistics", "tracker": true,
          "purpose": { "sv": "Mäter.", "en": "Measures." } }
      ]
    }
    """);

    private static CookieDeclarationCandidate Candidate(
        string name,
        string category = "necessary",
        CandidateFlag flag = CandidateFlag.None,
        ConsentPass pass = ConsentPass.Undecided)
        => new(
            Name: name,
            Provider: "Denna webbplats",
            Category: category,
            Purpose: "Syfte.",
            Duration: "Session",
            StorageType: "Cookie",
            Flag: flag,
            FirstSeenPass: pass,
            FirstSeenUrl: "https://ndstk.se/");

    [Fact]
    public void A_brand_new_cookie_is_added()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("newcookie")], [], EmptyCatalogue);

        Assert.Single(plan.ToAdd);
        Assert.Equal("newcookie", plan.ToAdd[0].Name);
        Assert.True(plan.HasWork);
    }

    [Fact]
    public void An_already_declared_cookie_is_not_added_again()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("UMB_MEMBER")], ["UMB_MEMBER"], EmptyCatalogue);

        Assert.Empty(plan.ToAdd);
        Assert.Contains("UMB_MEMBER", plan.AlreadyDeclared);
        Assert.False(plan.HasWork);
    }

    // The package seeds ".AspNetCore.Antiforgery.*". ASP.NET Core sets a suffixed real cookie. If
    // the existing pattern does not swallow it, every run re-adds the same cookie forever.
    [Fact]
    public void A_cookie_covered_by_an_existing_pattern_is_not_added()
    {
        MergePlan plan = MergePlanner.Plan(
            [Candidate(".AspNetCore.Antiforgery.CfDJ8Nf")],
            [".AspNetCore.Antiforgery.*"],
            EmptyCatalogue);

        Assert.Empty(plan.ToAdd);
        Assert.Contains(".AspNetCore.Antiforgery.*", plan.AlreadyDeclared);
    }

    [Fact]
    public void Matching_an_existing_declaration_ignores_case()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("umb_member")], ["UMB_MEMBER"], EmptyCatalogue);

        Assert.Empty(plan.ToAdd);
    }

    // Two observations of the same collapsed pattern - two Google Analytics properties - must
    // become one block, not two identical ones.
    [Fact]
    public void Two_candidates_with_the_same_name_collapse_to_one()
    {
        MergePlan plan = MergePlanner.Plan(
            [Candidate("_ga_*", "statistics"), Candidate("_ga_*", "statistics")],
            [],
            EmptyCatalogue);

        Assert.Single(plan.ToAdd);
    }

    // When the same pattern was seen in two passes, the earlier one wins - because that is the
    // one carrying the violation. Dropping it would hide the finding the scan exists to make.
    [Fact]
    public void Collapsing_keeps_the_earliest_pass_so_a_violation_survives()
    {
        MergePlan plan = MergePlanner.Plan(
            [
                Candidate("_ga_*", "statistics", CandidateFlag.None, ConsentPass.Statistics),
                Candidate("_ga_*", "statistics", CandidateFlag.Violation, ConsentPass.RejectAll),
            ],
            [],
            EmptyCatalogue);

        Assert.Single(plan.ToAdd);
        Assert.Equal(CandidateFlag.Violation, plan.ToAdd[0].Flag);
        Assert.Equal(ConsentPass.RejectAll, plan.ToAdd[0].FirstSeenPass);
    }

    // Reported, never deleted: a declaration can be perfectly correct and simply not have been
    // triggered by this crawl.
    [Fact]
    public void A_declaration_nothing_matched_is_reported_as_possibly_stale()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("UMB_MEMBER")], ["UMB_MEMBER", "old-cookie"], EmptyCatalogue);

        Assert.Contains("old-cookie", plan.DeclaredButNotFound);
        Assert.DoesNotContain("UMB_MEMBER", plan.DeclaredButNotFound);
    }

    [Fact]
    public void An_expected_catalogue_entry_the_scan_never_saw_is_reported()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("something_else")], [], ExpectingUmbMember);

        Assert.Contains("UMB_MEMBER", plan.ExpectedButNotObserved);
    }

    [Fact]
    public void An_expected_entry_the_scan_did_see_is_not_reported()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("UMB_MEMBER")], [], ExpectingUmbMember);

        Assert.Empty(plan.ExpectedButNotObserved);
    }

    // Only entries flagged expected count. An absent Google cookie is normal, not a finding.
    [Fact]
    public void An_unflagged_catalogue_entry_the_scan_never_saw_is_not_reported()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("UMB_MEMBER")], [], ExpectingUmbMember);

        Assert.DoesNotContain("_ga_*", plan.ExpectedButNotObserved);
    }

    [Fact]
    public void Fifty_new_declarations_are_within_the_cap()
    {
        IReadOnlyList<CookieDeclarationCandidate> candidates =
            Enumerable.Range(0, 50).Select(index => Candidate($"cookie{index}")).ToArray();

        MergePlan plan = MergePlanner.Plan(candidates, [], EmptyCatalogue);

        Assert.Equal(50, plan.ToAdd.Count);
        Assert.False(plan.ExceedsCap);
    }

    // Past the cap the endpoint refuses outright rather than writing the first fifty: a partial
    // apply leaves the page in a state nobody chose and makes the next run's diff meaningless.
    [Fact]
    public void Fifty_one_new_declarations_exceed_the_cap_without_being_truncated()
    {
        IReadOnlyList<CookieDeclarationCandidate> candidates =
            Enumerable.Range(0, 51).Select(index => Candidate($"cookie{index}")).ToArray();

        MergePlan plan = MergePlanner.Plan(candidates, [], EmptyCatalogue);

        Assert.Equal(51, plan.ToAdd.Count);
        Assert.True(plan.ExceedsCap);
    }

    [Fact]
    public void Nothing_found_and_nothing_declared_is_an_empty_plan()
    {
        MergePlan plan = MergePlanner.Plan([], [], EmptyCatalogue);

        Assert.Empty(plan.ToAdd);
        Assert.Empty(plan.AlreadyDeclared);
        Assert.Empty(plan.DeclaredButNotFound);
        Assert.False(plan.HasWork);
        Assert.False(plan.ExceedsCap);
    }

    // A blank declaration on the page is editor noise. It must not be treated as a pattern, or it
    // would swallow every candidate and the scan would silently report nothing new forever.
    [Fact]
    public void A_blank_existing_declaration_does_not_swallow_every_candidate()
    {
        MergePlan plan = MergePlanner.Plan([Candidate("newcookie")], ["", "   "], EmptyCatalogue);

        Assert.Single(plan.ToAdd);
    }

    [Fact]
    public void The_added_list_is_ordered_deterministically_by_name()
    {
        MergePlan plan = MergePlanner.Plan(
            [Candidate("zebra"), Candidate("alpha"), Candidate("mid")],
            [],
            EmptyCatalogue);

        Assert.Equal(["alpha", "mid", "zebra"], plan.ToAdd.Select(candidate => candidate.Name));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter MergePlannerTests`
Expected: build failure, `CS0103: The name 'MergePlanner' does not exist`.

- [ ] **Step 3: Write `MergePlan`**

`NDSTK.CookieScan.Core/MergePlan.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>
/// What a merge would do, worked out before anything is written.
/// </summary>
/// <remarks>
/// Deliberately a plan rather than an action: the tool prints it, the endpoint validates it, and
/// both work off exactly the same computation. Nothing here deletes or updates - every list other
/// than <paramref name="ToAdd"/> exists to be reported to a human.
/// </remarks>
public sealed record MergePlan(
    IReadOnlyList<CookieDeclarationCandidate> ToAdd,
    IReadOnlyList<string> AlreadyDeclared,
    IReadOnlyList<string> DeclaredButNotFound,
    IReadOnlyList<string> ExpectedButNotObserved)
{
    /// <summary>
    /// True when the plan proposes more blocks than one call may add. The endpoint turns this into
    /// a 400 and writes nothing: past this many, something is wrong with the scan or the
    /// catalogue, and half-applying it would be worse than refusing.
    /// </summary>
    public bool ExceedsCap => ToAdd.Count > MergePlanner.MaxBlocksPerCall;

    /// <summary>True when there is anything to write at all.</summary>
    public bool HasWork => ToAdd.Count > 0;
}
```

- [ ] **Step 4: Write `MergePlanner`**

`NDSTK.CookieScan.Core/MergePlanner.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>
/// Works out which proposed declarations are genuinely new, which are already on the page, and
/// what is worth telling a human about. Append-only by construction: there is no code path here
/// that removes or rewrites an existing declaration.
/// </summary>
public static class MergePlanner
{
    /// <summary>
    /// The most blocks one merge call may add. A backstop against a runaway scan bloating the
    /// node, not a paging limit - see <see cref="MergePlan.ExceedsCap"/>.
    /// </summary>
    public const int MaxBlocksPerCall = 50;

    public static MergePlan Plan(
        IEnumerable<CookieDeclarationCandidate> candidates,
        IEnumerable<string> declaredNames,
        CookieCatalogue catalogue)
    {
        // Blank declarations are editor noise. Left in, one would be read as a pattern, match
        // nothing, and land in DeclaredButNotFound on every run - or worse, if it were ever
        // treated as a wildcard, swallow every candidate silently.
        List<string> declared = declaredNames
            .Where(name => string.IsNullOrWhiteSpace(name) is false)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // One candidate per name. Where the same collapsed pattern was seen in more than one
        // pass, the earliest wins: that is the observation carrying a violation, and losing it
        // would hide the finding the scan exists to make.
        List<CookieDeclarationCandidate> unique = candidates
            .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(candidate => candidate.FirstSeenPass).First())
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToList();

        List<CookieDeclarationCandidate> toAdd = unique
            .Where(candidate => declared.Any(name => CookieNameMatcher.EitherMatches(name, candidate.Name)) is false)
            .ToList();

        List<string> alreadyDeclared = declared
            .Where(name => unique.Any(candidate => CookieNameMatcher.EitherMatches(name, candidate.Name)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Reported, never deleted. A declaration can be entirely correct and simply not have been
        // triggered by this crawl - a booking POST, a page the cap cut off, a seasonal embed.
        List<string> declaredButNotFound = declared
            .Except(alreadyDeclared, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // Only entries the catalogue flags as belonging to this site's own stack. An absent Google
        // cookie is normal; an absent antiforgery cookie means the crawl missed something.
        List<string> expectedButNotObserved = catalogue.Expected
            .Select(entry => entry.Pattern)
            .Where(pattern => unique.Any(candidate => CookieNameMatcher.EitherMatches(pattern, candidate.Name)) is false)
            .OrderBy(pattern => pattern, StringComparer.Ordinal)
            .ToList();

        return new MergePlan(toAdd, alreadyDeclared, declaredButNotFound, expectedButNotObserved);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter MergePlannerTests`
Expected: every test in the class passes.

- [ ] **Step 6: Verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded, 0 warnings.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: every test passes. This is the last task with no I/O, so from here on the suite is the regression net for everything above.
Run: `git status --short` — expected: the three files of this task and nothing else.

**Core is now complete.** Every rule in the spec is implemented and unit tested with no browser, no HTTP and no Umbraco. Tasks 6 onward add I/O around it.

---

## Task 6: Scanner project, CLI options and the Chromium bootstrap

**Files:**
- Create: `NDSTK.CookieScanner/NDSTK.CookieScanner.csproj`
- Create: `NDSTK.CookieScanner/ScanOptions.cs`
- Create: `NDSTK.CookieScanner/BrowserBootstrap.cs`
- Create: `NDSTK.CookieScanner/Program.cs` — a stub that parses, bootstraps and exits
- Modify: `NDSTK.slnx`

**Interfaces:**
- Consumes: `Locale` from Task 2.
- Produces:
  - `sealed record ScanOptions(Uri Url, Uri Target, int MaxPages, Locale Locale, string? MemberEmail, string? MemberPassword, string? ClientId, string? ClientSecret, bool DryRun, string ReportDir, bool Headed)` with `static ScanOptions Parse(string[] args)`, `bool WriteBackEnabled`
  - `static class BrowserBootstrap` with `static void EnsureChromium()`
- Tasks 7–13 consume `ScanOptions`.

**Playwright API note for the implementer.** Every Playwright member named in Tasks 6–9 was written from knowledge of the .NET binding, **not verified against the assembly** — the package is not in the local NuGet cache, so it could not be checked the way the Umbraco APIs were. After Step 1, run `dotnet build` and fix any member-name mismatch before continuing. The names to watch: `Microsoft.Playwright.Program.Main`, `IBrowserContext.APIRequest`, `BrowserContextCookiesResult.Expires`, `PageGotoOptions.WaitUntil`.

- [ ] **Step 1: Create the project and add Playwright**

`NDSTK.CookieScanner/NDSTK.CookieScanner.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>NDSTK.CookieScanner</RootNamespace>
    <AssemblyName>ndstk-cookiescan</AssemblyName>
    <InvariantGlobalization>false</InvariantGlobalization>
    <!--
      Published self-contained and single-file, which is what makes this a copy-anywhere exe.
      IncludeNativeLibrariesForSelfExtract is not optional: Playwright ships native libraries that
      a single-file bundle cannot load from memory, and without it the exe builds and then fails at
      the first browser launch.
    -->
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\NDSTK.CookieScan.Core\NDSTK.CookieScan.Core.csproj" />
  </ItemGroup>

</Project>
```

Then add Playwright and let the CLI pin the version rather than guessing one:

```bash
dotnet add NDSTK.CookieScanner/NDSTK.CookieScanner.csproj package Microsoft.Playwright
```

Add to `NDSTK.slnx`, keeping the list alphabetical:

```xml
  <Project Path="NDSTK.CookieScanner/NDSTK.CookieScanner.csproj" />
```

`NDSTK.csproj`'s `DefaultItemExcludes` already lists `NDSTK.CookieScanner\**` from Task 1. Confirm it does before building — if it does not, the web project will try to compile this exe's sources.

- [ ] **Step 2: Write `ScanOptions`**

`NDSTK.CookieScanner/ScanOptions.cs`:

```csharp
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>The parsed command line.</summary>
/// <remarks>
/// <paramref name="ClientSecret"/> comes from the environment, never from a flag: a secret passed
/// as an argument ends up in shell history and in any process listing.
/// </remarks>
public sealed record ScanOptions(
    Uri Url,
    Uri Target,
    int MaxPages,
    Locale Locale,
    string? MemberEmail,
    string? MemberPassword,
    string? ClientId,
    string? ClientSecret,
    bool DryRun,
    string ReportDir,
    bool Headed)
{
    public const string SecretVariable = "NDSTK_COOKIESCAN_CLIENT_SECRET";

    /// <summary>
    /// Whether anything will be written to Umbraco. Report-only is the safe default: a missing
    /// credential is not an error, it just means the scan reports and stops.
    /// </summary>
    public bool WriteBackEnabled
        => DryRun is false
            && string.IsNullOrWhiteSpace(ClientId) is false
            && string.IsNullOrWhiteSpace(ClientSecret) is false;

    public bool MemberScanEnabled
        => string.IsNullOrWhiteSpace(MemberEmail) is false
            && string.IsNullOrWhiteSpace(MemberPassword) is false;

    public static ScanOptions Parse(string[] args)
    {
        Dictionary<string, string?> flags = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal) is false)
            {
                continue;
            }

            string key = args[index][2..];
            bool hasValue = index + 1 < args.Length
                && args[index + 1].StartsWith("--", StringComparison.Ordinal) is false;

            flags[key] = hasValue ? args[++index] : null;
        }

        if (flags.TryGetValue("url", out string? url) is false || string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException(
                "--url is required. Example: ndstk-cookiescan --url https://ndstk.se");
        }

        var root = new Uri(url, UriKind.Absolute);

        return new ScanOptions(
            Url: root,
            Target: flags.TryGetValue("target", out string? target) && string.IsNullOrWhiteSpace(target) is false
                ? new Uri(target, UriKind.Absolute)
                : root,
            MaxPages: flags.TryGetValue("max-pages", out string? maxPages)
                && int.TryParse(maxPages, out int parsed) && parsed > 0
                ? parsed
                : 25,
            Locale: flags.TryGetValue("locale", out string? locale)
                && string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase)
                ? Locale.En
                : Locale.Sv,
            MemberEmail: Value(flags, "member-email"),
            MemberPassword: Value(flags, "member-password"),
            ClientId: Value(flags, "client-id"),
            ClientSecret: Environment.GetEnvironmentVariable(SecretVariable),
            DryRun: flags.ContainsKey("dry-run"),
            ReportDir: Value(flags, "report-dir") ?? Directory.GetCurrentDirectory(),
            Headed: flags.ContainsKey("headed"));

        static string? Value(Dictionary<string, string?> flags, string key)
            => flags.TryGetValue(key, out string? value) && string.IsNullOrWhiteSpace(value) is false
                ? value
                : null;
    }
}
```

- [ ] **Step 3: Write `BrowserBootstrap`**

`NDSTK.CookieScanner/BrowserBootstrap.cs`:

```csharp
namespace NDSTK.CookieScanner;

/// <summary>
/// Makes sure a Chromium build exists before the scan starts, fetching one if it does not.
/// </summary>
/// <remarks>
/// Chromium is not inside the exe - it lives in <c>%LOCALAPPDATA%\ms-playwright</c> and is roughly
/// 150MB. Doing this here rather than telling the user to run a separate install command is the
/// difference between a copy-anywhere exe and one with a setup ritual, and the message below is
/// why a first run appears to hang for a minute.
/// </remarks>
public static class BrowserBootstrap
{
    public static void EnsureChromium()
    {
        // Playwright's own installer is idempotent and cheap when the browser is already present,
        // so there is no need to probe for it first - and no need to guess at the cache path,
        // which differs per platform and per Playwright version.
        Console.WriteLine("Checking for a Chromium build...");

        int exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not install Chromium (Playwright exited {exitCode}). The first run on a "
                + "new machine downloads roughly 150MB, so this needs internet access. Once it has "
                + "succeeded, later runs reuse the copy in %LOCALAPPDATA%\\ms-playwright.");
        }
    }
}
```

- [ ] **Step 4: Write a stub `Program` so the project runs**

`NDSTK.CookieScanner/Program.cs`:

```csharp
using NDSTK.CookieScanner;

// Replaced in Task 10 with the real orchestration. For now it proves the CLI parses and Chromium
// can be provisioned, which are the two things every later task depends on.
try
{
    ScanOptions options = ScanOptions.Parse(args);

    Console.WriteLine($"Would scan {options.Url} (max {options.MaxPages} pages, locale {options.Locale}).");
    Console.WriteLine($"Write-back: {(options.WriteBackEnabled ? "enabled" : "disabled")}.");
    Console.WriteLine($"Member scan: {(options.MemberScanEnabled ? "enabled" : "disabled")}.");

    BrowserBootstrap.EnsureChromium();

    Console.WriteLine("Chromium is ready.");

    return 0;
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
```

- [ ] **Step 5: Build and run it**

Run: `dotnet build NDSTK.CookieScanner/NDSTK.CookieScanner.csproj`
Expected: build succeeded. **If any Playwright member name fails to resolve, fix it now** — see the API note above.

Run: `dotnet run --project NDSTK.CookieScanner -- --url https://ndstk.se --max-pages 5`
Expected: the three summary lines, then `Chromium is ready.` The first run downloads Chromium and takes a minute or two.

Run: `dotnet run --project NDSTK.CookieScanner`
Expected: `--url is required...` on stderr, exit code 2.

- [ ] **Step 6: Verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all Core tests still pass.
Run: `git status --short` — expected: the five files of this task. `NDSTK.CookieScanner/bin` and `obj` must not appear; if they do, the repository `.gitignore` needs no change and something is wrong with the paths.

---

## Task 7: `SiteCrawler` and `PageCapture`

**Files:**
- Create: `NDSTK.CookieScanner/SiteCrawler.cs`
- Create: `NDSTK.CookieScanner/PageCapture.cs`
- Modify: `NDSTK.CookieScanner/Program.cs` — print the discovered URL list

**Interfaces:**
- Consumes: `ScanOptions` from Task 6; `StorageKind` from Task 2.
- Produces:
  - `sealed class SiteCrawler` with `SiteCrawler(IPage page, ScanOptions options)` and `Task<IReadOnlyList<Uri>> DiscoverAsync(Uri from)`
  - `static class SiteCrawler.Exclusions` with `static bool IsExcluded(Uri candidate, Uri root)`
  - `sealed record CapturedEntry(string Name, StorageKind Storage, DateTimeOffset? Expires)`
  - `sealed record PageObservation(IReadOnlyList<CapturedEntry> Entries, IReadOnlySet<string> Hosts)`
  - `static class PageCapture` with `static Task<PageObservation> VisitAsync(IPage page, Uri url, ISet<string> hosts)`
- Tasks 8 and 9 consume all of these.

- [ ] **Step 1: Write `SiteCrawler`**

`NDSTK.CookieScanner/SiteCrawler.cs`:

```csharp
using Microsoft.Playwright;

namespace NDSTK.CookieScanner;

/// <summary>
/// Bounded breadth-first discovery of the site's own HTML pages.
/// </summary>
/// <remarks>
/// The list this produces is replayed identically by every consent pass. That is a correctness
/// requirement rather than an optimisation: if each pass discovered its own URLs, an entry
/// appearing "first in pass 4" might only mean pass 4 was the first to visit the page that sets
/// it, and every category inference downstream would be wrong.
/// </remarks>
public sealed class SiteCrawler(IPage page, ScanOptions options)
{
    public async Task<IReadOnlyList<Uri>> DiscoverAsync(Uri from)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<Uri> ordered = [];
        Queue<Uri> queue = new();

        queue.Enqueue(from);
        seen.Add(Normalise(from));

        while (queue.Count > 0 && ordered.Count < options.MaxPages)
        {
            Uri current = queue.Dequeue();

            IResponse? response;

            try
            {
                response = await page.GotoAsync(
                    current.ToString(),
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20_000 });
            }
            catch (PlaywrightException error)
            {
                // A page that will not load is worth a line, not an abort: one broken link must
                // not cost the whole scan.
                Console.Error.WriteLine($"  skipped {current} ({error.Message.Split('\n')[0]})");
                continue;
            }

            // Only HTML sets cookies through markup and script. A PDF or an image would just
            // burn a slot from the page cap.
            string contentType = response?.Headers.GetValueOrDefault("content-type") ?? string.Empty;

            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) is false)
            {
                continue;
            }

            ordered.Add(current);

            string[] hrefs = await page.EvalOnSelectorAllAsync<string[]>(
                "a[href]", "elements => elements.map(element => element.href)");

            foreach (string href in hrefs)
            {
                if (Uri.TryCreate(href, UriKind.Absolute, out Uri? link) is false
                    || Exclusions.IsExcluded(link, options.Url))
                {
                    continue;
                }

                if (seen.Add(Normalise(link)))
                {
                    queue.Enqueue(link);
                }
            }
        }

        return ordered;
    }

    // Fragments address a position on a page already queued, so keeping them would spend the page
    // cap revisiting the same document.
    private static string Normalise(Uri url)
        => new UriBuilder(url) { Fragment = string.Empty }.Uri.ToString().TrimEnd('/');

    /// <summary>What the crawl refuses to follow, and why.</summary>
    public static class Exclusions
    {
        // Following one of these mid-crawl would end the member session and quietly make every
        // later page in that pass anonymous - a whole pass of wrong results, with no error.
        private static readonly string[] SignOutSegments = ["logout", "logga-ut", "signout", "sign-out"];

        public static bool IsExcluded(Uri candidate, Uri root)
        {
            if (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
            {
                return true;
            }

            if (candidate.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase) is false)
            {
                return true;
            }

            // Backoffice cookies do not belong in a visitor-facing policy.
            if (candidate.AbsolutePath.StartsWith("/umbraco", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return SignOutSegments.Any(segment =>
                candidate.AbsolutePath.Contains(segment, StringComparison.OrdinalIgnoreCase));
        }
    }
}
```

- [ ] **Step 2: Write `PageCapture`**

`NDSTK.CookieScanner/PageCapture.cs`:

```csharp
using Microsoft.Playwright;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>One thing found in the browser, before a pass is attributed to it.</summary>
public sealed record CapturedEntry(string Name, StorageKind Storage, DateTimeOffset? Expires);

/// <summary>What one page visit produced.</summary>
public sealed record PageObservation(IReadOnlyList<CapturedEntry> Entries, IReadOnlySet<string> Hosts);

/// <summary>
/// Visits one page and reads back everything the browser now holds.
/// </summary>
public static class PageCapture
{
    public static async Task<PageObservation> VisitAsync(IPage page, Uri url, ISet<string> hosts)
    {
        try
        {
            // NetworkIdle rather than DOMContentLoaded: a third-party tag that sets a cookie
            // usually loads after the document is parsed, and stopping earlier would miss exactly
            // the cookies this tool exists to find.
            await page.GotoAsync(
                url.ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30_000 });
        }
        catch (PlaywrightException error)
        {
            Console.Error.WriteLine($"  {url} did not settle ({error.Message.Split('\n')[0]})");
        }

        List<CapturedEntry> entries = [];

        // Read from the context, not the page: a cookie set for the whole site by an earlier page
        // in this pass belongs to this pass, and reading per-page would keep re-finding it.
        foreach (BrowserContextCookiesResult cookie in await page.Context.CookiesAsync())
        {
            entries.Add(new CapturedEntry(
                cookie.Name,
                StorageKind.Cookie,
                // Playwright reports -1 for a session cookie.
                cookie.Expires < 0 ? null : DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires)));
        }

        entries.AddRange(await KeysAsync(page, "localStorage", StorageKind.LocalStorage));
        entries.AddRange(await KeysAsync(page, "sessionStorage", StorageKind.SessionStorage));

        return new PageObservation(entries, new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<CapturedEntry>> KeysAsync(
        IPage page, string store, StorageKind kind)
    {
        try
        {
            string[] keys = await page.EvaluateAsync<string[]>($"() => Object.keys({store})");

            // Neither store has an expiry; DurationFormatter decides the wording from the kind.
            return keys.Select(key => new CapturedEntry(key, kind, null)).ToArray();
        }
        catch (PlaywrightException)
        {
            // Storage access throws on a page served from an opaque origin, and on an error page.
            // Nothing to report - it is an absence of data, not a fault.
            return [];
        }
    }

    /// <summary>
    /// Records the host of every request the page makes, for the report's third-party section.
    /// Attach once per context, before any navigation.
    /// </summary>
    public static void RecordHosts(IPage page, ISet<string> hosts, Uri root)
    {
        page.Request += (_, request) =>
        {
            if (Uri.TryCreate(request.Url, UriKind.Absolute, out Uri? uri)
                && uri.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase) is false)
            {
                hosts.Add(uri.Host);
            }
        };
    }
}
```

- [ ] **Step 3: Wire discovery into `Program` temporarily**

Replace the body of `NDSTK.CookieScanner/Program.cs` between `BrowserBootstrap.EnsureChromium();` and `return 0;` with:

```csharp
    using IPlaywright playwright = await Microsoft.Playwright.Playwright.CreateAsync();

    await using IBrowser browser = await playwright.Chromium.LaunchAsync(
        new BrowserTypeLaunchOptions { Headless = options.Headed is false });

    // IgnoreHTTPSErrors so a scan of a local site behind a dev certificate works without the
    // operator having to trust it first.
    await using IBrowserContext context = await browser.NewContextAsync(
        new BrowserNewContextOptions { IgnoreHTTPSErrors = true });

    IPage page = await context.NewPageAsync();

    IReadOnlyList<Uri> urls = await new SiteCrawler(page, options).DiscoverAsync(options.Url);

    Console.WriteLine($"Discovered {urls.Count} page(s):");

    foreach (Uri url in urls)
    {
        Console.WriteLine($"  {url}");
    }
```

and add `using Microsoft.Playwright;` at the top of the file.

- [ ] **Step 4: Run it against the site**

**The site must be running for this step.** Stop here and ask the user to start it, then run:

Run: `dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44300 --max-pages 10`
(substituting the port from `Properties/launchSettings.json`)

Expected: a list of the site's own pages — start, articles, login, the cookie policy page. Confirm by eye that **no `/umbraco` URL and no logout URL appears**, and that the count respects `--max-pages`.

- [ ] **Step 5: Verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all Core tests pass.
Run: `git status --short` — expected: the three files of this task.

---

## Task 8: `ConsentPassRunner` and earliest-pass reduction

**Files:**
- Create: `NDSTK.CookieScan.Core/ObservedEntries.cs`
- Create: `NDSTK.CookieScanner/ConsentPassRunner.cs`
- Test: `NDSTK.Tests/ObservedEntriesTests.cs`
- Modify: `NDSTK.CookieScanner/Program.cs` — run the six passes and print what each found

**Interfaces:**
- Consumes: `ScanOptions` (Task 6), `SiteCrawler`/`PageCapture`/`CapturedEntry` (Task 7), `ConsentPass`/`ConsentPasses`/`ObservedEntry`/`StorageKind` (Task 2).
- Produces:
  - `static class ObservedEntries` with `static IReadOnlyList<ObservedEntry> EarliestPerName(IEnumerable<ObservedEntry> entries)`
  - `sealed record PassEntry(string Name, StorageKind Storage, DateTimeOffset? Expires, Uri FirstUrl)`
  - `sealed record PassResult(ConsentPass Pass, IReadOnlyList<PassEntry> Entries, IReadOnlySet<string> Hosts)`
  - `sealed class ConsentPassRunner` with `ConsentPassRunner(IBrowser browser, ScanOptions options, string endpointPath)` and `Task<PassResult> RunAsync(ConsentPass pass, IReadOnlyList<Uri> urls)`
- Tasks 9 and 10 consume all of these.

- [ ] **Step 1: Write the failing test for earliest-pass reduction**

This is the rule the entire design rests on, so it gets a test rather than being trusted inside an I/O class.

`NDSTK.Tests/ObservedEntriesTests.cs`:

```csharp
using NDSTK.CookieScan.Core;

namespace NDSTK.Tests;

public class ObservedEntriesTests
{
    private static ObservedEntry Entry(string name, ConsentPass pass, StorageKind storage = StorageKind.Cookie)
        => new(name, storage, pass, $"https://ndstk.se/{pass}", null);

    // The same cookie appears in every pass from the one that set it onwards. Only the first
    // appearance carries information about which category it belongs to.
    [Fact]
    public void The_earliest_pass_wins_regardless_of_input_order()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("_ga_ABC", ConsentPass.AcceptAll),
            Entry("_ga_ABC", ConsentPass.Statistics),
            Entry("_ga_ABC", ConsentPass.Marketing),
        ]);

        Assert.Single(reduced);
        Assert.Equal(ConsentPass.Statistics, reduced[0].FirstSeenPass);
    }

    [Fact]
    public void The_url_of_the_earliest_appearance_is_kept()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("cookie", ConsentPass.AcceptAll),
            Entry("cookie", ConsentPass.Undecided),
        ]);

        Assert.Equal("https://ndstk.se/Undecided", reduced[0].FirstSeenUrl);
    }

    // A localStorage key and a cookie can legitimately share a name, and they are different
    // declarations with different durations. Collapsing them would lose one.
    [Fact]
    public void The_same_name_in_two_storage_kinds_stays_two_entries()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("theme", ConsentPass.Preferences, StorageKind.Cookie),
            Entry("theme", ConsentPass.Preferences, StorageKind.LocalStorage),
        ]);

        Assert.Equal(2, reduced.Count);
    }

    // The member dimension runs last and visits different URLs. A cookie seen in both must be
    // attributed to the public pass that saw it first, not to the member area.
    [Fact]
    public void A_public_pass_beats_the_member_dimension()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("cookie", ConsentPass.MemberArea),
            Entry("cookie", ConsentPass.RejectAll),
        ]);

        Assert.Equal(ConsentPass.RejectAll, reduced[0].FirstSeenPass);
    }

    [Fact]
    public void Matching_names_ignores_case()
    {
        IReadOnlyList<ObservedEntry> reduced = ObservedEntries.EarliestPerName(
        [
            Entry("UMB_MEMBER", ConsentPass.AcceptAll),
            Entry("umb_member", ConsentPass.Undecided),
        ]);

        Assert.Single(reduced);
        Assert.Equal(ConsentPass.Undecided, reduced[0].FirstSeenPass);
    }

    [Fact]
    public void An_empty_input_is_an_empty_result()
    {
        Assert.Empty(ObservedEntries.EarliestPerName([]));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ObservedEntriesTests`
Expected: build failure, `CS0103: The name 'ObservedEntries' does not exist`.

- [ ] **Step 3: Write `ObservedEntries`**

`NDSTK.CookieScan.Core/ObservedEntries.cs`:

```csharp
namespace NDSTK.CookieScan.Core;

/// <summary>
/// Reduces every observation of a name across all passes to the single earliest one.
/// </summary>
/// <remarks>
/// A cookie set in the reject-all pass is still present in every later pass, so without this the
/// same cookie would be classified once per pass and the loosest classification could win. The
/// earliest appearance is the only one that carries information: it is the least consent under
/// which the site was still willing to set the thing.
/// </remarks>
public static class ObservedEntries
{
    public static IReadOnlyList<ObservedEntry> EarliestPerName(IEnumerable<ObservedEntry> entries)
        => entries
            .GroupBy(entry => (entry.Name.ToLowerInvariant(), entry.Storage))
            .Select(group => group.OrderBy(entry => entry.FirstSeenPass).First())
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter ObservedEntriesTests`
Expected: every test in the class passes.

- [ ] **Step 5: Write `ConsentPassRunner`**

`NDSTK.CookieScanner/ConsentPassRunner.cs`:

```csharp
using Microsoft.Playwright;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>One thing a pass found, with the URL it first turned up on.</summary>
public sealed record PassEntry(string Name, StorageKind Storage, DateTimeOffset? Expires, Uri FirstUrl);

/// <summary>Everything one pass produced.</summary>
public sealed record PassResult(
    ConsentPass Pass,
    IReadOnlyList<PassEntry> Entries,
    IReadOnlySet<string> Hosts);

/// <summary>
/// Runs one consent pass: a clean browser context, a real decision posted to the site, then the
/// fixed URL list replayed with everything the browser holds read back after each page.
/// </summary>
public sealed class ConsentPassRunner(IBrowser browser, ScanOptions options, string endpointPath)
{
    public async Task<PassResult> RunAsync(ConsentPass pass, IReadOnlyList<Uri> urls)
    {
        // A fresh context per pass is what makes "first seen in this pass" mean anything: the
        // cookie jar starts empty, so nothing carries over from the pass before.
        await using IBrowserContext context = await browser.NewContextAsync(
            new BrowserNewContextOptions { IgnoreHTTPSErrors = true });

        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);
        IPage page = await context.NewPageAsync();

        PageCapture.RecordHosts(page, hosts, options.Url);

        await DecideAsync(context, pass);

        Dictionary<(string Name, StorageKind Storage), PassEntry> found = [];

        foreach (Uri url in urls)
        {
            PageObservation observation = await PageCapture.VisitAsync(page, url, hosts);

            foreach (CapturedEntry entry in observation.Entries)
            {
                // First URL wins - it is the page that actually caused the thing to be set, and
                // that is what makes a report line actionable.
                found.TryAdd(
                    (entry.Name, entry.Storage),
                    new PassEntry(entry.Name, entry.Storage, entry.Expires, url));
            }
        }

        return new PassResult(pass, [.. found.Values], hosts);
    }

    /// <summary>
    /// Posts the pass's decision to the site's own consent endpoint.
    /// </summary>
    /// <remarks>
    /// Through the context's API request, not <c>AddCookiesAsync</c>: the package writes the
    /// consent cookie server-side precisely so its attributes are right, and a hand-forged cookie
    /// risks a shape the site rejects. If that happened the scan would silently measure the
    /// undecided state six times over and report a clean bill of health.
    /// </remarks>
    private async Task DecideAsync(IBrowserContext context, ConsentPass pass)
    {
        object? decision = DecisionFor(pass);

        if (decision is null)
        {
            return;
        }

        // Load the root first so the context has an origin the cookie can be stored against.
        IPage warmUp = await context.NewPageAsync();
        await warmUp.GotoAsync(options.Url.ToString(), new PageGotoOptions { Timeout = 30_000 });
        await warmUp.CloseAsync();

        string endpoint = new Uri(options.Url, endpointPath).ToString();

        IAPIResponse response = await context.APIRequest.PostAsync(
            endpoint, new APIRequestContextOptions { DataObject = decision });

        if (response.Status == 429)
        {
            throw new InvalidOperationException(
                $"The consent endpoint throttled pass {pass} (HTTP 429). The passes must run "
                + "sequentially and the site's Esatto:CookieBanner:ThrottleRequestsPerMinute must "
                + "be at least 7. Raise it, or wait a minute and re-run.");
        }

        if (response.Ok is false)
        {
            throw new InvalidOperationException(
                $"The consent endpoint returned HTTP {response.Status} for pass {pass} at "
                + $"{endpoint}. Check that app.UseCookieConsent() is mapped and that EndpointPath "
                + "matches the site's configuration.");
        }
    }

    // accept-all sends the full category list explicitly: the package's endpoint grants exactly
    // the set it is given and deliberately does not read "all" from an omission.
    private static object? DecisionFor(ConsentPass pass) => pass switch
    {
        ConsentPass.Undecided => null,
        ConsentPass.RejectAll => new { action = "reject-all", categories = Array.Empty<string>() },
        ConsentPass.Preferences => new { action = "custom", categories = new[] { "preferences" } },
        ConsentPass.Statistics => new { action = "custom", categories = new[] { "statistics" } },
        ConsentPass.Marketing => new { action = "custom", categories = new[] { "marketing" } },
        ConsentPass.AcceptAll or ConsentPass.MemberArea =>
            new { action = "accept-all", categories = new[] { "preferences", "statistics", "marketing" } },
        _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, null),
    };
}
```

- [ ] **Step 6: Run the six passes from `Program`**

In `NDSTK.CookieScanner/Program.cs`, after the discovery block from Task 7, add:

```csharp
    // The endpoint path the site actually uses. The package default; override with --endpoint-path
    // is deliberately not offered, because a site that has moved it has also moved its own JS.
    const string ConsentEndpointPath = "/api/cookie-consent";

    var runner = new ConsentPassRunner(browser, options, ConsentEndpointPath);
    List<ObservedEntry> observed = [];
    Dictionary<ConsentPass, IReadOnlySet<string>> hostsByPass = [];

    foreach (ConsentPass pass in ConsentPasses.Comparable)
    {
        Console.WriteLine($"Pass {(int)pass + 1}/6: {pass}...");

        PassResult result = await runner.RunAsync(pass, urls);

        hostsByPass[pass] = result.Hosts;

        foreach (PassEntry entry in result.Entries)
        {
            observed.Add(new ObservedEntry(
                entry.Name, entry.Storage, pass, entry.FirstUrl.ToString(), entry.Expires));
        }

        Console.WriteLine($"  {result.Entries.Count} entr(ies), {result.Hosts.Count} third-party host(s)");
    }

    IReadOnlyList<ObservedEntry> earliest = ObservedEntries.EarliestPerName(observed);

    Console.WriteLine($"\n{earliest.Count} distinct entr(ies) across all passes:");

    foreach (ObservedEntry entry in earliest)
    {
        Console.WriteLine($"  {entry.Name} [{entry.Storage}] first seen in {entry.FirstSeenPass}");
    }
```

and add `using NDSTK.CookieScan.Core;` at the top.

- [ ] **Step 7: Run it against the site**

**The site must be running.** Stop and ask the user to start it, then run:

Run: `dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44300 --max-pages 8`

Expected: six pass lines, then a distinct-entry list. On this site's current state, expect the consent cookie and the antiforgery cookie first seen in `Undecided` or `RejectAll`, and — per the spec's stated limitation — **no** `.AspNetCore.Mvc.CookieTempDataProvider`, because no crawl step POSTs a form.

If a pass fails with HTTP 429, raise `Esatto:CookieBanner:ThrottleRequestsPerMinute` in `appsettings.Development.json` to 20 and re-run. That is the spec's risk 4, and this is the step that settles it.

- [ ] **Step 8: Verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all tests pass.
Run: `git status --short` — expected: the four files of this task, plus `appsettings.Development.json` only if Step 7 required the throttle change.

---

## Task 9: `MemberDimension`

**Files:**
- Create: `NDSTK.CookieScanner/MemberDimension.cs`
- Modify: `NDSTK.CookieScanner/Program.cs` — run it when credentials are supplied

**Interfaces:**
- Consumes: `ScanOptions` (Task 6), `SiteCrawler`/`PageCapture` (Task 7), `PassEntry`/`PassResult`/`ConsentPassRunner` (Task 8).
- Produces: `sealed class MemberDimension` with `MemberDimension(IBrowser browser, ScanOptions options, string endpointPath)` and `Task<PassResult> RunAsync()`.
- Task 10 consumes the `PassResult`.

- [ ] **Step 1: Write `MemberDimension`**

`NDSTK.CookieScanner/MemberDimension.cs`:

```csharp
using Microsoft.Playwright;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>
/// The signed-in dimension: log in, then discover and visit the member area.
/// </summary>
/// <remarks>
/// Its own discovery rather than a replay of the public URL list, because the pages of interest -
/// the portal, bookings, children - are only linked once signed in. That is also why this sits
/// outside the six comparable passes: it visits a different URL set, so its findings cannot be
/// compared by pass order against them.
/// <para>
/// Login is the only form this submits. Nothing here POSTs a booking, a cancellation or a
/// payment: the scanner must not be able to create real records on a live site, which is why the
/// TempData cookie stays a documented limitation rather than something to chase.
/// </para>
/// </remarks>
public sealed class MemberDimension(IBrowser browser, ScanOptions options, string endpointPath)
{
    public async Task<PassResult> RunAsync()
    {
        await using IBrowserContext context = await browser.NewContextAsync(
            new BrowserNewContextOptions { IgnoreHTTPSErrors = true });

        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);
        IPage page = await context.NewPageAsync();

        PageCapture.RecordHosts(page, hosts, options.Url);

        // Accept everything, so a cookie found here is attributable to the login rather than to a
        // consent state this dimension did not mean to test.
        IAPIResponse decision = await context.APIRequest.PostAsync(
            new Uri(options.Url, endpointPath).ToString(),
            new APIRequestContextOptions
            {
                DataObject = new
                {
                    action = "accept-all",
                    categories = new[] { "preferences", "statistics", "marketing" },
                },
            });

        if (decision.Ok is false)
        {
            throw new InvalidOperationException(
                $"Could not record consent for the member dimension (HTTP {decision.Status}).");
        }

        Uri? portal = await SignInAsync(page);

        if (portal is null)
        {
            Console.Error.WriteLine(
                "  Member login did not appear to succeed - skipping the member dimension. Check "
                + "the credentials, and that the account is activated.");

            return new PassResult(ConsentPass.MemberArea, [], hosts);
        }

        IReadOnlyList<Uri> memberUrls = await new SiteCrawler(page, options).DiscoverAsync(portal);

        Dictionary<(string, StorageKind), PassEntry> found = [];

        foreach (Uri url in memberUrls)
        {
            PageObservation observation = await PageCapture.VisitAsync(page, url, hosts);

            foreach (CapturedEntry entry in observation.Entries)
            {
                found.TryAdd(
                    (entry.Name, entry.Storage),
                    new PassEntry(entry.Name, entry.Storage, entry.Expires, url));
            }
        }

        return new PassResult(ConsentPass.MemberArea, [.. found.Values], hosts);
    }

    /// <summary>
    /// Submits the login form and returns the URL it landed on, or null when it did not sign in.
    /// </summary>
    /// <remarks>
    /// Success is judged by the UMB_MEMBER cookie existing, not by the landing URL: the site's
    /// login controller returns the same page on failure with a ModelState error, so a URL check
    /// would read a rejected password as a success and then crawl the public site again, reporting
    /// nothing new and no error.
    /// </remarks>
    private async Task<Uri?> SignInAsync(IPage page)
    {
        // The login page is found rather than assumed: its URL is editor-owned content.
        IReadOnlyList<Uri> publicUrls = await new SiteCrawler(page, options).DiscoverAsync(options.Url);

        Uri? loginUrl = publicUrls.FirstOrDefault(url =>
            url.AbsolutePath.Contains("logga-in", StringComparison.OrdinalIgnoreCase)
            || url.AbsolutePath.Contains("login", StringComparison.OrdinalIgnoreCase));

        if (loginUrl is null)
        {
            Console.Error.WriteLine("  No login page found in the crawl.");
            return null;
        }

        await page.GotoAsync(loginUrl.ToString(), new PageGotoOptions { Timeout = 30_000 });

        // Name attributes, matching Views/Login.cshtml's inputs.
        await page.FillAsync("input[name='Email']", options.MemberEmail!);
        await page.FillAsync("input[name='Password']", options.MemberPassword!);
        await page.ClickAsync("button[type='submit'], input[type='submit']");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        bool signedIn = (await page.Context.CookiesAsync())
            .Any(cookie => cookie.Name.Equals("UMB_MEMBER", StringComparison.OrdinalIgnoreCase));

        return signedIn ? new Uri(page.Url) : null;
    }
}
```

- [ ] **Step 2: Wire it into `Program`**

In `NDSTK.CookieScanner/Program.cs`, immediately after the `foreach` over `ConsentPasses.Comparable` and **before** the `ObservedEntries.EarliestPerName` call:

```csharp
    if (options.MemberScanEnabled)
    {
        Console.WriteLine("Member dimension: signing in...");

        PassResult member = await new MemberDimension(browser, options, ConsentEndpointPath).RunAsync();

        hostsByPass[ConsentPass.MemberArea] = member.Hosts;

        foreach (PassEntry entry in member.Entries)
        {
            observed.Add(new ObservedEntry(
                entry.Name, entry.Storage, ConsentPass.MemberArea,
                entry.FirstUrl.ToString(), entry.Expires));
        }

        Console.WriteLine($"  {member.Entries.Count} entr(ies) in the member area");
    }
```

- [ ] **Step 3: Run it with credentials**

**The site must be running, and a test member account must exist and be activated.** Ask the user for a throwaway member's email and password, or to create one, then run:

Run:
```bash
dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44300 --max-pages 8 \
  --member-email <address> --member-password <password>
```

Expected: the six passes as before, then `Member dimension: signing in...` and a non-zero entry count. `UMB_MEMBER` must now appear in the distinct list with `first seen in MemberArea`.

If it reports that login did not succeed, check the account is activated — the site's login controller refuses an unverified account.

- [ ] **Step 4: Verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all tests pass.
Run: `git status --short` — expected: the two files of this task.

---

## Task 10: `ScanReportWriter`, orchestration and exit codes

**Files:**
- Create: `NDSTK.CookieScanner/ScanReportWriter.cs`
- Modify: `NDSTK.CookieScanner/ScanOptions.cs` — split `WriteBackEnabled` into `CanReachApi` and the dry-run flag
- Modify: `NDSTK.CookieScanner/Program.cs` — the real orchestration

**Interfaces:**
- Consumes: everything from Tasks 2–9.
- Produces:
  - `sealed record MergeOutcome(IReadOnlyList<string> Added, IReadOnlyList<string> AlreadyDeclared, IReadOnlyList<string> DeclaredButNotFound, bool Saved)`
  - `static class ScanReportWriter` with `static int Write(ScanOptions options, IReadOnlyList<CookieDeclarationCandidate> candidates, IReadOnlyList<string> expectedButNotObserved, IReadOnlyDictionary<ConsentPass, IReadOnlySet<string>> hostsByPass, MergeOutcome? outcome)` returning the process exit code
- Task 13 consumes `MergeOutcome`; Task 14 reads the report files.

**Refinement to Task 6.** `WriteBackEnabled` folded the dry-run flag into the credential check, which turns out to be wrong: a `--dry-run` run **with** credentials is the most useful mode there is — it plans against the real page and reports exactly what would be added, writing nothing. Split into `CanReachApi` (credentials present) with `DryRun` passed through to the endpoint.

- [ ] **Step 1: Adjust `ScanOptions`**

In `NDSTK.CookieScanner/ScanOptions.cs`, replace the `WriteBackEnabled` property with:

```csharp
    /// <summary>
    /// Whether the endpoint can be called at all. Report-only is the safe default: a missing
    /// credential is not an error, it just means the scan cannot compare itself against the page.
    /// </summary>
    public bool CanReachApi
        => string.IsNullOrWhiteSpace(ClientId) is false
            && string.IsNullOrWhiteSpace(ClientSecret) is false;
```

and update the stub `Program`'s summary line from `options.WriteBackEnabled` to `options.CanReachApi`.

- [ ] **Step 2: Write `ScanReportWriter`**

`NDSTK.CookieScanner/ScanReportWriter.cs`:

```csharp
using System.Text;
using System.Text.Json;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>What the merge endpoint reported back, or null when it was never called.</summary>
public sealed record MergeOutcome(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> AlreadyDeclared,
    IReadOnlyList<string> DeclaredButNotFound,
    bool Saved);

/// <summary>
/// Writes the console summary and the two report files, and decides the process exit code.
/// </summary>
public static class ScanReportWriter
{
    public const int ExitClean = 0;
    public const int ExitViolations = 1;
    public const int ExitError = 2;

    public static int Write(
        ScanOptions options,
        IReadOnlyList<CookieDeclarationCandidate> candidates,
        IReadOnlyList<string> expectedButNotObserved,
        IReadOnlyDictionary<ConsentPass, IReadOnlySet<string>> hostsByPass,
        MergeOutcome? outcome)
    {
        List<CookieDeclarationCandidate> violations =
            [.. candidates.Where(candidate => candidate.Flag == CandidateFlag.Violation)];

        List<CookieDeclarationCandidate> needsReview =
            [.. candidates.Where(candidate => candidate.Flag == CandidateFlag.NeedsReview)];

        var markdown = new StringBuilder();

        markdown.AppendLine("# Cookie scan report");
        markdown.AppendLine();
        markdown.AppendLine($"- Site: {options.Url}");
        markdown.AppendLine($"- Pages per pass: up to {options.MaxPages}");
        markdown.AppendLine($"- Member dimension: {(options.MemberScanEnabled ? "yes" : "no")}");
        markdown.AppendLine($"- Write-back: {Describe(options, outcome)}");
        markdown.AppendLine();

        // Violations first, deliberately. It is the finding that matters, and burying it under a
        // table of forty ordinary cookies is how a compliance problem goes unread.
        Section(markdown, "Violations", violations.Select(candidate =>
            $"**{candidate.Name}** — categorised `{candidate.Category}`, but was set during the "
            + $"`{candidate.FirstSeenPass}` pass, which did not grant it. First seen at {candidate.FirstSeenUrl}"));

        if (outcome is not null)
        {
            Section(markdown, "Added to the policy page (draft)", outcome.Added);
            Section(markdown, "Already declared", outcome.AlreadyDeclared);
            Section(
                markdown,
                "Declared but not found — reported, never deleted",
                outcome.DeclaredButNotFound);
        }
        else
        {
            markdown.AppendLine("## Comparison against the policy page");
            markdown.AppendLine();
            markdown.AppendLine(
                "Not performed. Pass `--client-id` and set "
                + $"`{ScanOptions.SecretVariable}` to compare the scan against what the page "
                + "already declares. Add `--dry-run` to compare without writing anything.");
            markdown.AppendLine();
        }

        Section(markdown, "Needs review — only ever seen with everything granted", needsReview.Select(
            candidate => $"{candidate.Name} — written as `{candidate.Category}`, which is a fallback"));

        Section(markdown, "Expected but not observed", expectedButNotObserved);

        markdown.AppendLine("## All entries found");
        markdown.AppendLine();
        markdown.AppendLine("| Name | Storage | Category | First seen in | Duration |");
        markdown.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (CookieDeclarationCandidate candidate in candidates)
        {
            markdown.AppendLine(
                $"| `{candidate.Name}` | {candidate.StorageType} | {candidate.Category} "
                + $"| {candidate.FirstSeenPass} | {candidate.Duration} |");
        }

        markdown.AppendLine();

        Section(markdown, "Third-party hosts contacted", hostsByPass
            .Where(pass => pass.Value.Count > 0)
            .Select(pass => $"{pass.Key}: {string.Join(", ", pass.Value.Order())}"));

        Directory.CreateDirectory(options.ReportDir);

        string markdownPath = Path.Combine(options.ReportDir, "cookie-scan-report.md");
        string jsonPath = Path.Combine(options.ReportDir, "cookie-scan-report.json");

        File.WriteAllText(markdownPath, markdown.ToString());
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(
            new
            {
                site = options.Url.ToString(),
                violations,
                needsReview,
                expectedButNotObserved,
                candidates,
                merge = outcome,
                hosts = hostsByPass.ToDictionary(pass => pass.Key.ToString(), pass => pass.Value.Order()),
            },
            new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine($"{candidates.Count} entr(ies) found.");

        if (violations.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  {violations.Count} CONSENT VIOLATION(S):");

            foreach (CookieDeclarationCandidate violation in violations)
            {
                Console.WriteLine(
                    $"    {violation.Name} ({violation.Category}) was set during the "
                    + $"{violation.FirstSeenPass} pass, which did not grant it.");
            }
        }

        if (outcome is not null)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"  {outcome.Added.Count} added, {outcome.AlreadyDeclared.Count} already declared, "
                + $"{outcome.DeclaredButNotFound.Count} declared but not found.");

            if (outcome.Saved)
            {
                Console.WriteLine(
                    "  The policy page was saved as a DRAFT. Review the new blocks in the "
                    + "backoffice and publish when you are happy with the wording.");
            }
        }

        if (expectedButNotObserved.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "  Expected but not observed: " + string.Join(", ", expectedButNotObserved));
        }

        Console.WriteLine();
        Console.WriteLine($"Report written to {markdownPath}");
        Console.WriteLine($"                  {jsonPath}");

        // Exit code reflects findings, never configuration. A report-only run that found a
        // violation still fails, so a missing credential can never mask one.
        return violations.Count > 0 ? ExitViolations : ExitClean;
    }

    private static string Describe(ScanOptions options, MergeOutcome? outcome)
        => outcome switch
        {
            null when options.CanReachApi is false => "not configured (report only)",
            null => "attempted but failed - see the console output",
            { Saved: true } => "saved as a draft",
            _ => options.DryRun ? "dry run, nothing written" : "nothing new to write",
        };

    private static void Section(StringBuilder markdown, string title, IEnumerable<string> lines)
    {
        List<string> materialised = [.. lines];

        markdown.AppendLine($"## {title}");
        markdown.AppendLine();

        if (materialised.Count == 0)
        {
            markdown.AppendLine("_None._");
        }
        else
        {
            foreach (string line in materialised)
            {
                markdown.AppendLine($"- {line}");
            }
        }

        markdown.AppendLine();
    }
}
```

- [ ] **Step 3: Replace `Program` with the real orchestration**

`NDSTK.CookieScanner/Program.cs`, in full:

```csharp
using Microsoft.Playwright;
using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

// The package's default consent endpoint. Not a flag: a site that has moved it has also moved its
// own JavaScript, so a mismatch here would be the least of that site's problems.
const string ConsentEndpointPath = "/api/cookie-consent";

try
{
    ScanOptions options = ScanOptions.Parse(args);

    Console.WriteLine($"Scanning {options.Url} - up to {options.MaxPages} pages per pass, locale {options.Locale}.");

    BrowserBootstrap.EnsureChromium();

    CookieCatalogue catalogue = LoadCatalogue();

    using IPlaywright playwright = await Playwright.CreateAsync();

    await using IBrowser browser = await playwright.Chromium.LaunchAsync(
        new BrowserTypeLaunchOptions { Headless = options.Headed is false });

    IReadOnlyList<Uri> urls;

    // Discovery runs in its own throwaway context so the pages it loads cannot leave cookies in
    // any pass's jar.
    await using (IBrowserContext discovery = await browser.NewContextAsync(
        new BrowserNewContextOptions { IgnoreHTTPSErrors = true }))
    {
        urls = await new SiteCrawler(await discovery.NewPageAsync(), options).DiscoverAsync(options.Url);
    }

    if (urls.Count == 0)
    {
        Console.Error.WriteLine(
            $"Found no HTML pages at {options.Url}. Is the site running, and is the URL right?");

        return ScanReportWriter.ExitError;
    }

    Console.WriteLine($"Discovered {urls.Count} page(s). Running {ConsentPasses.Comparable.Count} passes.");

    var runner = new ConsentPassRunner(browser, options, ConsentEndpointPath);
    List<ObservedEntry> observed = [];
    Dictionary<ConsentPass, IReadOnlySet<string>> hostsByPass = [];

    foreach (ConsentPass pass in ConsentPasses.Comparable)
    {
        Console.WriteLine($"  pass {(int)pass + 1}/{ConsentPasses.Comparable.Count}: {pass}");

        PassResult result = await runner.RunAsync(pass, urls);

        hostsByPass[pass] = result.Hosts;
        observed.AddRange(result.Entries.Select(entry => new ObservedEntry(
            entry.Name, entry.Storage, pass, entry.FirstUrl.ToString(), entry.Expires)));
    }

    if (options.MemberScanEnabled)
    {
        Console.WriteLine("  member dimension: signing in");

        PassResult member = await new MemberDimension(browser, options, ConsentEndpointPath).RunAsync();

        hostsByPass[ConsentPass.MemberArea] = member.Hosts;
        observed.AddRange(member.Entries.Select(entry => new ObservedEntry(
            entry.Name, entry.Storage, ConsentPass.MemberArea,
            entry.FirstUrl.ToString(), entry.Expires)));
    }

    DateTimeOffset now = DateTimeOffset.UtcNow;

    IReadOnlyList<CookieDeclarationCandidate> candidates = ObservedEntries
        .EarliestPerName(observed)
        .Select(entry => CategoryInference.Classify(entry, catalogue, now, options.Locale))
        .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderBy(candidate => candidate.FirstSeenPass).First())
        .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
        .ToArray();

    // Computed here rather than taken from the endpoint: it depends on THIS run's catalogue, which
    // may be an override file the site knows nothing about.
    IReadOnlyList<string> expectedButNotObserved =
        [.. MergePlanner.Plan(candidates, [], catalogue).ExpectedButNotObserved];

    MergeOutcome? outcome = null;

    if (options.CanReachApi)
    {
        outcome = await new ManagementApiClient(options).MergeAsync(candidates);
    }

    return ScanReportWriter.Write(options, candidates, expectedButNotObserved, hostsByPass, outcome);
}
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);

    return ScanReportWriter.ExitError;
}
catch (Exception error)
{
    Console.Error.WriteLine($"The scan failed: {error.Message}");

    return ScanReportWriter.ExitError;
}

// An override beside the exe replaces the embedded catalogue wholesale, so legal wording can be
// changed without a rebuild.
static CookieCatalogue LoadCatalogue()
{
    string beside = Path.Combine(AppContext.BaseDirectory, "cookie-catalogue.json");

    if (File.Exists(beside))
    {
        Console.WriteLine($"Using the catalogue override at {beside}.");

        return CookieCatalogue.Parse(File.ReadAllText(beside));
    }

    return CookieCatalogue.Default();
}
```

This references `ManagementApiClient`, which arrives in Task 13. **Until then the project will not compile** — that is expected, and Tasks 11 and 12 do not need it to. If you want a green build in between, comment out the `if (options.CanReachApi)` block and restore it in Task 13.

- [ ] **Step 4: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all Core tests pass, unaffected.
Run: `git status --short` — expected: the three files of this task.

Do not attempt `dotnet build NDSTK.slnx` until Task 13 supplies `ManagementApiClient`.

---

## Task 11: The site-side merge endpoint

**Files:**
- Create: `CookieScan/CookieScanContracts.cs`
- Create: `CookieScan/CookieScanWriter.cs`
- Create: `CookieScan/CookieScanController.cs`
- Modify: `NDSTK.csproj` — reference `NDSTK.CookieScan.Core`
- Modify: `Program.cs` (site) — register the writer

**Interfaces:**
- Consumes: `CookieDeclarationCandidate`, `MergePlan`, `MergePlanner`, `CookieCatalogue`, `ConsentPass`, `CandidateFlag` from Core.
- Produces:
  - `sealed record CookieScanDeclaration(string Name, string Provider, string Category, string Purpose, string Duration, string StorageType)`
  - `sealed record CookieScanMergeRequest(IReadOnlyList<CookieScanDeclaration> Declarations, bool DryRun)`
  - `sealed record CookieScanMergeResponse(IReadOnlyList<string> Added, IReadOnlyList<string> AlreadyDeclared, IReadOnlyList<string> DeclaredButNotFound, Guid PolicyPageKey, bool Saved)`
  - `sealed class CookieScanWriter` with `CookieScanMergeResponse Merge(CookieScanMergeRequest request)`
- Task 13 consumes the two DTO shapes over the wire.

**Verification this task settles (spec risks 1 and 3).** The authorisation policy constant, whether an API-user token satisfies it, and whether `Expose` really is required for block visibility. All three fail at runtime rather than at compile time.

- [ ] **Step 1: Reference Core from the web project**

In `NDSTK.csproj`, the existing project-reference group becomes:

```xml
  <ItemGroup>
    <ProjectReference Include="NDSTK.Domain\NDSTK.Domain.csproj" />
    <ProjectReference Include="NDSTK.CookieScan.Core\NDSTK.CookieScan.Core.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Write the contracts**

`CookieScan/CookieScanContracts.cs`:

```csharp
namespace NDSTK.CookieScan;

/// <summary>One declaration the scanner proposes, as it arrives over the wire.</summary>
public sealed record CookieScanDeclaration(
    string Name,
    string Provider,
    string Category,
    string Purpose,
    string Duration,
    string StorageType);

/// <summary>
/// A merge request. <paramref name="DryRun"/> plans and reports without saving, which is what lets
/// an operator see exactly what would change before allowing it.
/// </summary>
public sealed record CookieScanMergeRequest(
    IReadOnlyList<CookieScanDeclaration> Declarations,
    bool DryRun = false);

public sealed record CookieScanMergeResponse(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> AlreadyDeclared,
    IReadOnlyList<string> DeclaredButNotFound,
    Guid PolicyPageKey,
    bool Saved);
```

- [ ] **Step 3: Write `CookieScanWriter`**

`CookieScan/CookieScanWriter.cs`:

```csharp
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
    /// Finds the policy page the same way the package does: the configured key when set, otherwise
    /// the first node of the policy document type.
    /// </summary>
    /// <remarks>
    /// The package's own resolver is internal, so this repeats the rule rather than calling it.
    /// Note the deliberate absence of <c>contentService.GetById(Guid)</c>: Umbraco 18.1.1 declares
    /// only the int overload on IContentService, so the key is resolved through IEntityService
    /// first - which is identical across 17 and 18.
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
                    $"Esatto:CookieBanner:PolicyPageKey points at {configured}, which is not a "
                    + $"published '{PolicyAlias}' node.");
        }

        IContentType policyType = contentTypeService.Get(PolicyAlias)
            ?? throw new RejectedException($"No '{PolicyAlias}' document type exists.");

        IContent? found = contentService
            .GetPagedOfType(policyType.Id, 0, ScanPageSize, out _, null, null)
            .FirstOrDefault();

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

        return jsonSerializer.Deserialize<BlockListValue>(raw)
            ?? throw new RejectedException(
                "The policy page's 'cookies' value could not be read as a Block List. Refusing to "
                + "overwrite it - open the page in the backoffice and check it saves cleanly first.");
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
```

- [ ] **Step 4: Write the controller**

`CookieScan/CookieScanController.cs`:

```csharp
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace NDSTK.CookieScan;

/// <summary>
/// The one endpoint the cookie scanner posts its findings to.
/// </summary>
/// <remarks>
/// A narrow, site-owned endpoint rather than the generic document endpoint, because
/// <c>UpdateDocumentRequestModel</c> makes a document PUT a whole-document replace: an omitted
/// property is erased, so a client rebuilding the payload from outside could silently blank the
/// policy page's introduction or outro. Here the merge happens server-side with Umbraco's own
/// Block List types, and the only thing that can be touched is one property of one node.
/// </remarks>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("cookie-scan")]
[ApiExplorerSettings(GroupName = "Cookie scan")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
public sealed class CookieScanController(CookieScanWriter writer) : ManagementApiControllerBase
{
    [HttpPost("merge")]
    [ProducesResponseType(typeof(CookieScanMergeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Merge(CookieScanMergeRequest request)
    {
        try
        {
            return Ok(writer.Merge(request));
        }
        catch (CookieScanWriter.RejectedException rejected)
        {
            // Everything the caller could fix comes back as a 400 with the reason in plain text,
            // because the caller is a command-line tool printing it straight to an operator.
            return BadRequest(new { message = rejected.Message });
        }
    }
}
```

- [ ] **Step 5: Register the writer in the site's `Program.cs`**

In `Program.cs`, immediately after the `builder.CreateUmbracoBuilder()` chain and before `builder.Build()`:

```csharp
// The cookie scanner's merge endpoint. Scoped, because it uses IContentService.
builder.Services.AddScoped<NDSTK.CookieScan.CookieScanWriter>();
```

- [ ] **Step 6: Build, then verify the endpoint exists**

Run: `dotnet build NDSTK.csproj`
Expected: build succeeded. **If `AuthorizationPolicies.BackOfficeAccess` or `VersionedApiBackOfficeRoute` fails to resolve, fix the `using` before continuing** — both were confirmed present in the 18.1.1 assembly, so this is a namespace question rather than an availability one. `Asp.Versioning` comes transitively with the Management API package; drop the `[ApiVersion]` attribute if it does not resolve.

**Ask the user to start the site**, then confirm the route is mapped: open `/umbraco/swagger` and look for the "Cookie scan" group with `POST /umbraco/management/api/v1/cookie-scan/merge`.

- [ ] **Step 7: Note that the API user comes next**

No manual backoffice step. Task 12 creates the API user in code, so the client id and secret are values you choose rather than a secret shown once in the UI that has to be copied by hand.

- [ ] **Step 8: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all Core tests pass.
Run: `git status --short` — expected: the five files of this task.

The endpoint cannot be exercised end to end until Task 12 supplies an API user and Task 13 supplies a client. Task 14 does that verification, including the `Expose` question.

---

## Task 12: The API user seeder

**Files:**
- Create: `CookieScan/CookieScanApiUser.cs`
- Create: `CookieScan/CookieScanApiUserSeeder.cs`
- Modify: `Program.cs` (site) — register the seeder as a hosted startup task
- Modify: `appsettings.Development.json` — enable it
- Modify: `appsettings.Secrets.json` — the secret (gitignored; the user edits this)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `sealed class CookieScanApiUserSeeder` with `Task SeedAsync(CancellationToken cancellationToken)`, and `sealed class CookieScanApiUserOptions` bound from `NDSTK:CookieScanApiUser`.
- Task 13 uses the client id and secret this creates; Task 14 verifies them end to end.

**Why this exists.** The alternative is a manual backoffice step — Users → API Users → Create, then copy a secret shown exactly once. Doing it in code means the credentials are values chosen up front, so the scanner and the site read the same secret from the same place and nothing has to be transcribed.

**Verified API surface**, checked against `Umbraco.Core.xml` and the Management API assembly at 18.1.1 rather than taken from documentation:
- `IUserService.CreateAsync(Guid performingUserKey, UserCreateModel model, bool approveUser)`
- `UserCreateModel` with `Email`, `Id`, `Kind`, `Name`, `UserName`, `UserGroupKeys`
- `UserKind.Api`
- `IUserService.AddClientIdAsync(Guid userKey, string clientId)` and `FindByClientIdAsync`
- `IBackOfficeApplicationManager.EnsureBackOfficeClientCredentialsApplicationAsync`

Note that `ClientCredentialsFlowSettings` in configuration is **not** the route: it belongs to `DeliveryApiSettings` and concerns members and the Delivery API, not Management API users.

**Security, stated plainly.** This creates a real credential with content access. It is therefore opt-in — nothing happens unless both the enable flag and a secret are configured — and it must not be switched on in production unless the user actually wants that account to exist there. The seeder logs the client id it created, never the secret.

- [ ] **Step 1: Write the options type**

`CookieScan/CookieScanApiUser.cs`:

```csharp
namespace NDSTK.CookieScan;

/// <summary>
/// Configuration for the cookie scanner's API user, bound from <c>NDSTK:CookieScanApiUser</c>.
/// </summary>
/// <remarks>
/// Opt-in by construction: with <see cref="Enabled"/> false or <see cref="ClientSecret"/> blank,
/// the seeder does nothing at all. This creates a credential with content access, so it must never
/// appear by default on an environment nobody asked for it on.
/// </remarks>
public sealed class CookieScanApiUserOptions
{
    public const string SectionName = "NDSTK:CookieScanApiUser";

    public bool Enabled { get; set; }

    public string ClientId { get; set; } = "cookie-scanner";

    /// <summary>Belongs in appsettings.Secrets.json, which is gitignored, or an environment variable.</summary>
    public string? ClientSecret { get; set; }

    public string Name { get; set; } = "Cookie scanner";

    public string Email { get; set; } = "cookie-scanner@ndstk.local";

    /// <summary>
    /// The user group aliases the API user joins. Content access is what the merge endpoint's
    /// authorisation requires; nothing here needs Settings or Users.
    /// </summary>
    public string[] UserGroupAliases { get; set; } = ["editor"];
}
```

- [ ] **Step 2: Write the seeder**

`CookieScan/CookieScanApiUserSeeder.cs`:

```csharp
using Microsoft.Extensions.Options;
using Umbraco.Cms.Api.Management.Security;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.OperationStatus;

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

                await userService.AddClientIdAsync(userKey.Value, settings.ClientId);
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
```

**Two things to verify while writing this**, both runtime rather than compile failures:
- `IUserGroupService.GetAsync(string alias)` — confirm the member name and that `editor` is the right alias on this site. If the site has no `editor` group, check what `uSync/v18/` or the backoffice actually calls it and change the default.
- The exact generic arguments of what `CreateAsync` returns, and the property that carries the new user. Adjust the `Attempt<,>` and `attempt.Result` lines to whatever the assembly declares; the shape above is the expected one but was not compiled.

- [ ] **Step 3: Register it in the site's `Program.cs`**

Beside the `CookieScanWriter` registration from Task 11:

```csharp
builder.Services.Configure<NDSTK.CookieScan.CookieScanApiUserOptions>(
    builder.Configuration.GetSection(NDSTK.CookieScan.CookieScanApiUserOptions.SectionName));
builder.Services.AddScoped<NDSTK.CookieScan.CookieScanApiUserSeeder>();
```

Then run it once the runtime is up. Immediately after `await app.BootUmbracoAsync();` and before `app.UseCookieConsent();`:

```csharp
// Creates the cookie scanner's API user when configured to. After BootUmbracoAsync because it
// needs the user service, and awaited rather than fire-and-forget so a failure is logged in order
// rather than interleaved with the first request.
using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider
        .GetRequiredService<NDSTK.CookieScan.CookieScanApiUserSeeder>()
        .SeedAsync(CancellationToken.None);
}
```

- [ ] **Step 4: Configure it for development only**

In `appsettings.Development.json`, add at the top level:

```json
  "NDSTK": {
    "CookieScanApiUser": {
      "Enabled": true,
      "ClientId": "cookie-scanner"
    }
  }
```

Deliberately **not** in `appsettings.json`: this creates a credential, and it should exist on a developer machine because someone asked for it there, not everywhere by default.

Then ask the user to put the secret in `appsettings.Secrets.json`, which is already gitignored:

```json
  "NDSTK": {
    "CookieScanApiUser": {
      "ClientSecret": "<a long random string of their choosing>"
    }
  }
```

Tell them the same value goes in `NDSTK_COOKIESCAN_CLIENT_SECRET` when they run the scanner, and that it must be long — this is a credential with content access, not a placeholder.

- [ ] **Step 5: Build, then let the user restart the site**

Run: `dotnet build NDSTK.csproj`
Expected: build succeeded. Fix any member-name mismatch from the Step 2 note now.

**Stop and ask the user to restart the site**, then to confirm in the log output:

```
The cookie scanner's API user is ready with client id cookie-scanner.
```

If instead it logs that no group exists, fix the `UserGroupAliases` default to a group this site has.

- [ ] **Step 6: Confirm the user exists**

Ask the user to check **Users → API Users** in the backoffice and confirm `Cookie scanner` is listed. The secret is not shown there, and does not need to be — it is in their secrets file.

- [ ] **Step 7: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all Core tests pass.
Run: `git status --short` — expected: the two new `CookieScan/` files, `Program.cs`, and `appsettings.Development.json`. **`appsettings.Secrets.json` must NOT appear** — it is gitignored, and if git lists it, stop and fix that before anything else.

---

## Task 13: `ManagementApiClient`

**Files:**
- Create: `NDSTK.CookieScanner/ManagementApiClient.cs`
- Modify: `NDSTK.CookieScanner/Program.cs` — restore the `if (options.CanReachApi)` block if it was commented out in Task 10

**Interfaces:**
- Consumes: `ScanOptions` (Task 6), `MergeOutcome` (Task 10), `CookieDeclarationCandidate` (Task 4).
- Produces: `sealed class ManagementApiClient` with `ManagementApiClient(ScanOptions options)` and `Task<MergeOutcome?> MergeAsync(IReadOnlyList<CookieDeclarationCandidate> candidates)`.

**Verification this task settles (spec risks 1 and 2).** Whether an API-user client-credentials token satisfies the backoffice-access policy, and the token endpoint's exact request encoding.

- [ ] **Step 1: Write `ManagementApiClient`**

`NDSTK.CookieScanner/ManagementApiClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>
/// Gets an API-user token, then posts the scan's declarations to the site's merge endpoint.
/// </summary>
/// <remarks>
/// A failure here is reported and swallowed rather than thrown: the scan's findings are worth
/// having even when the write-back cannot happen, and a violation must still fail the run on its
/// own merits. <see cref="MergeAsync"/> returns null in that case and the report says so.
/// </remarks>
public sealed class ManagementApiClient(ScanOptions options)
{
    private const string TokenPath = "/umbraco/management/api/v1/security/back-office/token";
    private const string MergePath = "/umbraco/management/api/v1/cookie-scan/merge";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<MergeOutcome?> MergeAsync(IReadOnlyList<CookieDeclarationCandidate> candidates)
    {
        using HttpClient http = CreateClient();

        try
        {
            string token = await TokenAsync(http);

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var request = new
            {
                declarations = candidates.Select(candidate => new
                {
                    name = candidate.Name,
                    provider = candidate.Provider,
                    category = candidate.Category,
                    purpose = candidate.Purpose,
                    duration = candidate.Duration,
                    storageType = candidate.StorageType,
                }),
                dryRun = options.DryRun,
            };

            using HttpResponseMessage response = await http.PostAsJsonAsync(MergePath, request, Json);

            string body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode is false)
            {
                Console.Error.WriteLine(
                    $"  The merge endpoint returned HTTP {(int)response.StatusCode}: {body}");

                return null;
            }

            MergeResponse? parsed = JsonSerializer.Deserialize<MergeResponse>(body, Json);

            if (parsed is null)
            {
                Console.Error.WriteLine("  The merge endpoint returned a body that could not be read.");

                return null;
            }

            return new MergeOutcome(
                parsed.Added ?? [],
                parsed.AlreadyDeclared ?? [],
                parsed.DeclaredButNotFound ?? [],
                parsed.Saved);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Console.Error.WriteLine($"  Write-back failed: {error.Message}");

            return null;
        }
    }

    private async Task<string> TokenAsync(HttpClient http)
    {
        // Form-encoded, as the OAuth client-credentials grant specifies. If the endpoint turns out
        // to want JSON, that is spec risk 2 and this is the line to change.
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = options.ClientId!,
            ["client_secret"] = options.ClientSecret!,
        });

        using HttpResponseMessage response = await http.PostAsync(TokenPath, form);

        string body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode is false)
        {
            throw new InvalidOperationException(
                $"Could not get a token (HTTP {(int)response.StatusCode}). Check the client id, and "
                + $"that {ScanOptions.SecretVariable} holds the matching secret. Response: {body}");
        }

        TokenResponse? token = JsonSerializer.Deserialize<TokenResponse>(body, Json);

        return string.IsNullOrWhiteSpace(token?.AccessToken)
            ? throw new InvalidOperationException("The token response contained no access_token.")
            : token.AccessToken;
    }

    private HttpClient CreateClient()
    {
        var handler = new HttpClientHandler();

        // Only for a loopback target, and only so a scan of a local site behind a dev certificate
        // works without the operator having to trust it first. Deliberately not extended to a real
        // host: silently accepting any certificate when talking to production, while sending a
        // client secret, would be indefensible.
        if (options.Target.IsLoopback)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        return new HttpClient(handler)
        {
            BaseAddress = options.Target,
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record MergeResponse(
        IReadOnlyList<string>? Added,
        IReadOnlyList<string>? AlreadyDeclared,
        IReadOnlyList<string>? DeclaredButNotFound,
        bool Saved);
}
```

- [ ] **Step 2: Restore the write-back call**

Confirm `NDSTK.CookieScanner/Program.cs` contains, uncommented:

```csharp
    if (options.CanReachApi)
    {
        outcome = await new ManagementApiClient(options).MergeAsync(candidates);
    }
```

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build NDSTK.slnx`
Expected: build succeeded, 0 errors. This is the first point since Task 10 at which everything compiles together.

- [ ] **Step 4: Verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: all Core tests pass.
Run: `git status --short` — expected: the two files of this task.

---

## Task 14: End-to-end verification and the portable exe

**Files:**
- Create: `docs/cookie-scanner.md`
- No source changes expected. Any needed here is a fix to an earlier task, applied in place.

**Interfaces:** none produced. This task consumes everything.

- [ ] **Step 1: Dry run against the real site with credentials**

**The site must be running, and the API user from Task 12 must exist.** Use the same secret you put in `appsettings.Secrets.json`, then run a dry run — nothing is written, but the full comparison happens:

```bash
export NDSTK_COOKIESCAN_CLIENT_SECRET='<the secret>'
dotnet run --project NDSTK.CookieScanner -- \
  --url https://localhost:44300 \
  --client-id cookie-scanner \
  --dry-run \
  --report-dir ./scan-out
```

Expected:
- Six pass lines, then a findings summary.
- `Write-back: dry run, nothing written` in the report header.
- `already declared` includes the consent cookie and `.AspNetCore.Antiforgery.*` — proving pattern matching against the seeded declarations works, which is the single most important behaviour in the merge.
- `Expected but not observed` names `.AspNetCore.Mvc.CookieTempDataProvider`, exactly as the spec predicts.
- Exit code 0. Check with `echo $?`.

**If the token request fails with 401**, that is spec risk 1 resolving badly: the API-user token does not satisfy `BackOfficeAccess`. Fix it in `CookieScanController` by relaxing to a policy that admits API users, and record which one in `docs/cookie-scanner.md`. Do not work around it by adding a shared secret — the audit trail is the reason this endpoint is authorised at all.

- [ ] **Step 2: Confirm nothing was written**

Ask the user to open the cookie policy page in the backoffice and confirm the Block List is unchanged and the page has no unpublished changes. A dry run that writes is the worst possible bug in this tool.

- [ ] **Step 3: Real run**

Same command **without** `--dry-run`:

```bash
dotnet run --project NDSTK.CookieScanner -- \
  --url https://localhost:44300 \
  --client-id cookie-scanner \
  --report-dir ./scan-out
```

Expected: `The policy page was saved as a DRAFT.` if anything new was found; `nothing new to write` if the page already covers everything.

- [ ] **Step 4: Confirm the blocks render — this settles spec risk 3**

Ask the user to open the cookie policy page in the backoffice.

Expected: any new declaration appears as a **visible block** in the Cookies list with its name, provider, category, purpose, duration and storage type filled in, and the page shows unpublished changes.

**If the page saved but the new blocks do not appear**, `Expose` is the cause — confirm `CookieScanWriter.Append` adds a `BlockItemVariation` per block. That is the failure mode the package's own seeder comment warns about, and it is silent.

Also confirm the page's **introduction and outro text are untouched**. That is the whole-document-replace trap the design avoided; this is where it would show.

- [ ] **Step 5: Confirm idempotence**

Run the exact same command a second time.

Expected: `0 added`, and every name from the first run now in `already declared`. A second run that adds anything means the merge is not idempotent and the matching in `MergePlanner` needs revisiting before this tool is used again.

- [ ] **Step 6: Confirm the violation exit code**

There are no trackers on this site, so a violation has to be induced. Add a temporary line to `Views/Root.cshtml` inside `<head>`, **above** `<consent-head />` so it is not gated:

```cshtml
<script>document.cookie = "_fbp=test; path=/";</script>
```

Ask the user to restart the site, then run the scan with `--dry-run`.

Expected: `1 CONSENT VIOLATION(S)` naming `_fbp` set during the `Undecided` pass, the report's Violations section populated, and **exit code 1** (`echo $?`).

Then **remove that line** and confirm `git diff Views/Root.cshtml` is empty before finishing.

- [ ] **Step 7: Publish the portable exe**

```bash
dotnet publish NDSTK.CookieScanner -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Expected: `dist/ndstk-cookiescan.exe`, roughly 80MB.

Verify it runs standalone from a directory that is not the repository:

```bash
cd /c/Users/carl_ && /c/src/NDSTK/dist/ndstk-cookiescan.exe --url https://ndstk.se --max-pages 3 --dry-run
```

Expected: it finds Chromium, crawls, and writes a report into the current directory. If it fails at browser launch, `IncludeNativeLibrariesForSelfExtract` is missing from the csproj.

Add `dist/` to `.gitignore` if it is not already covered — an 80MB binary must not be committed.

- [ ] **Step 8: Write the usage documentation**

`docs/cookie-scanner.md`, covering: what the tool does and does not do; the six passes and what each one proves; the full flag list from the spec's CLI table; how the API user is created and how to rotate its secret; the environment variable for the secret; the publish command for the exe; how to override the catalogue with a file beside the exe; the exit codes; and the two stated limitations — the TempData cookie and no pixel detection. Record here the actual authorisation policy used, if Step 1 forced a change, and state plainly that `NDSTK:CookieScanApiUser:Enabled` creates a credential with content access and belongs in development configuration only.

- [ ] **Step 9: Final verification checkpoint**

Run: `dotnet build NDSTK.slnx` — expected: build succeeded, 0 warnings.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — expected: every test passes.
Run: `git status --short` — expected: `docs/cookie-scanner.md`, plus `.gitignore` if Step 7 needed it. `dist/` must not be listed. `Views/Root.cshtml` must not be listed.

Report to the user: the scan's findings, what was added to the policy page as a draft, and that it is waiting for them to publish.

---

## Plan Self-Review

**1. Spec coverage.** Every section of the spec maps to a task:

| Spec section | Task |
| --- | --- |
| Architecture, three projects, registration chores | 1, 6, 11 |
| URL discovery once, replayed | 7, 10 |
| Six passes, clean context, real decision posted | 8 |
| Capture: cookies, localStorage, sessionStorage, hosts | 7 |
| Member area, optional | 9 |
| Category inference, earliest pass wins | 4, 8 |
| The violation rule, generalised across passes 1–5 | 4 |
| Catalogue: format, most-specific-wins, `expected`, seed contents | 2 |
| Duration formatting, both locales, plurals, floor of 1 | 3 |
| Write-back: token, endpoint contract, server-side merge | 11, 13 |
| The API user the write-back authenticates as | 12 |
| The `Expose` / dropdown-array / Save-not-Publish specifics | 11, verified in 14 Step 4 |
| Guardrails including the 50 cap | 5, 11 |
| Idempotence via bidirectional glob matching | 1, 5, verified in 14 Step 5 |
| Report: seven sections, exit codes | 10, verified in 14 Steps 1 and 6 |
| CLI surface | 6, 10 |
| The portable exe and Chromium bootstrap | 6, 14 Step 7 |
| Testing list | 1–5, 8 |
| Risks 1 and 2 (auth policy, token encoding) | 13, 14 Step 1 |
| Risk 3 (`Expose` required) | 14 Step 4 |
| Risk 4 (throttle) | 8 Step 7 |
| Limitations | 2 (catalogue `expected`), 9, 10, documented in 14 Step 8 |

No gaps found. Task 12 is additional to the spec rather than covering a section of it: the spec assumed the API user would be created by hand in the backoffice, and doing it in code removes that manual step. `IUserService.CreateAsync`, `UserKind.Api`, `AddClientIdAsync` and `EnsureBackOfficeClientCredentialsApplicationAsync` were all confirmed present at 18.1.1 before the task was written.

**2. Placeholder scan.** No "TBD", no "add appropriate error handling", no "write tests for the above", no "similar to Task N". Every code step carries the actual code. Three places state honestly that something is unverified rather than pretending otherwise — the Playwright member names (Task 6), the authorisation policy (Tasks 11 and 13), and the user-group lookup and `Attempt<,>` shapes in the seeder (Task 12) — and each names the step that settles it and what to do if it resolves badly.

**3. Type consistency.** Three inconsistencies were found while reviewing and are fixed above:

- Task 6 defined `ScanOptions.WriteBackEnabled` folding in `DryRun`; Task 10 needs credentials-present and dry-run to be separable, so Task 10 Step 1 explicitly replaces it with `CanReachApi` and says why. Every later reference uses `CanReachApi`.
- `MergePlanner.Plan` requires a `CookieCatalogue`, but the server has no business computing `ExpectedButNotObserved` from the scanner's possibly-overridden catalogue. Rather than change Core's signature, Task 11 passes `CookieCatalogue.Default()` and documents that it ignores that one field of the plan.
- `PassEntry` carries `Uri FirstUrl` while `ObservedEntry` carries `string FirstSeenUrl`. The conversion is `entry.FirstUrl.ToString()` and appears identically in Tasks 8, 9 and 10.

Signatures cross-checked between producer and consumer: `CookieNameMatcher` (1 → 2, 5); `CookieCatalogue`/`ConsentPasses`/`ObservedEntry`/`StorageKinds` (2 → 4, 5, 8, 11); `DurationFormatter`/`Wording` (3 → 4); `CookieDeclarationCandidate`/`CandidateFlag` (4 → 5, 10, 11); `MergePlan`/`MergePlanner` (5 → 10, 11); `ScanOptions` (6 → 7–13); `SiteCrawler`/`PageCapture`/`CapturedEntry` (7 → 8, 9); `PassResult`/`PassEntry` (8 → 9, 10); `MergeOutcome` (10 → 13); `CookieScanApiUserOptions` (12 → 14, via configuration rather than code).
