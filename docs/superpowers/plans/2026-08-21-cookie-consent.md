# Cookie Consent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a self-hosted cookie consent platform and cookie policy page for the NDSTK Umbraco 18 site, replacing a Cookietractor subscription.

**Architecture:** Consent lives in a first-party JSON cookie written by the server. Script gating is primarily server-side — a Razor tag helper simply does not emit non-consented tags, so there is no client-side race to lose. A small dependency-free banner handles the visitor's choice and unblocks `type="text/plain"` scripts without a reload. The cookie registry is Umbraco content on the policy page, so editors maintain it and the table renders server-side.

**Tech Stack:** .NET 10, Umbraco CMS 18.1.1, xUnit v3 (3.2.2), vanilla JS, no client-side dependencies.

**Spec:** `docs/superpowers/specs/2026-08-21-cookie-consent-design.md`

## Global Constraints

- Target framework `net10.0`; Umbraco `18.1.1`; nullable enabled; implicit usings enabled.
- **No client-side dependencies and no CDN references.** A consent tool making a third-party request undercuts its own purpose. Everything self-hosted under `wwwroot/static/`.
- **Views must not use ModelsBuilder-generated types.** `ModelsMode` is `Nothing` in production. Use `Model.Value<T>("alias")` only.
- **Razor generic method calls must be wrapped in `@(...)`** — `@Model.Value<string>("x")` is parsed as an HTML tag and fails to compile.
- **All file paths are case-exact.** Deployment target is Linux; `Views/Partials/blocklist/Components/` and alias casing matter.
- Content types created by the installer are **culture-invariant** (`ContentVariation.Nothing`).
- Installer methods are **create-if-missing**: never rewrite existing schema.
- Consent cookie: name `ndstk-consent`, `Path=/`, `SameSite=Lax`, `Secure` under HTTPS, **not** `HttpOnly`, 365-day lifetime.
- Consent log stores **no IP address, no user agent, no member id**. This is a deliberate design decision, not an omission.
- Accept and reject controls must have **equal visual weight** — same dimensions and padding, differing only in colour.
- Existing palette tokens only: `--primary #001F54`, `--accent #F7E300`, `--bg #E5E6E8`, `--text #222`, `--muted #666`. No dark mode.

## Deviation from the spec, found during plan self-review

Spec §3 bundles the `POST /api/consent` endpoint with the consent log, and §13 puts the consent log at build-order stage 7. But the banner (stage 5) cannot function without the endpoint, because §2 requires the **server** to write the cookie.

**Resolution:** the endpoint ships in this plan (Task 3) and sets the cookie only. Stage 7 later adds log-row writing inside the existing endpoint. Update spec §3 to say so.

## Scope

Build-order stages 1–6 from spec §13. Explicitly **not** in this plan: the consent log table and migration infrastructure (stage 7), and the drift detector and backoffice dashboard (stage 8).

---

### Task 1: Test project, consent model, cookie codec

**Files:**
- Create: `tests/NDSTK.Tests/NDSTK.Tests.csproj`
- Modify: `NDSTK.slnx`
- Create: `Consent/ConsentCategory.cs`
- Create: `Consent/ConsentCategories.cs`
- Create: `Consent/ConsentAction.cs`
- Create: `Consent/ConsentDecision.cs`
- Create: `Consent/ConsentCookieCodec.cs`
- Test: `tests/NDSTK.Tests/Consent/ConsentCookieCodecTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum ConsentCategory { Necessary, Preferences, Statistics, Marketing }`; `enum ConsentAction { AcceptAll, RejectAll, Custom, Withdrawn }`; `static class ConsentCategories` with `IReadOnlyList<ConsentCategory> Consentable`, `string ToWireName(ConsentCategory)`, `bool TryParse(string?, out ConsentCategory)`; `sealed record ConsentDecision(int PolicyVersion, DateTimeOffset DecidedAt, IReadOnlySet<ConsentCategory> Granted, string ConsentId)` with `bool HasGranted(ConsentCategory)` and `bool NeedsRePrompt(int currentPolicyVersion)`; `static class ConsentCookieCodec` with `string Encode(ConsentDecision)`, `ConsentDecision? Decode(string?)`, `string NewConsentId()`.

- [ ] **Step 1: Create the test project**

`tests/NDSTK.Tests/NDSTK.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../NDSTK.csproj" />
  </ItemGroup>
</Project>
```

Note: xUnit v3 test projects are executables — `<OutputType>Exe</OutputType>` is required, not optional.

- [ ] **Step 2: Add the test project to the solution**

Replace `NDSTK.slnx` with:

```xml
<Solution>
  <Project Path="NDSTK.csproj" />
  <Project Path="tests/NDSTK.Tests/NDSTK.Tests.csproj" />
</Solution>
```

- [ ] **Step 3: Verify the empty test project builds**

Run: `dotnet build tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: `Build succeeded`. If `xunit.runner.visualstudio` 3.1.5 does not resolve, run `dotnet add tests/NDSTK.Tests package xunit.runner.visualstudio` and take whatever version it picks.

- [ ] **Step 4: Write the failing tests**

`tests/NDSTK.Tests/Consent/ConsentCookieCodecTests.cs`:

```csharp
using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

public class ConsentCookieCodecTests
{
    private static ConsentDecision Decision(params ConsentCategory[] granted)
        => new(1, new DateTimeOffset(2026, 8, 21, 9, 12, 33, TimeSpan.Zero), granted.ToHashSet(), "abc123");

    [Fact]
    public void Round_trips_a_decision()
    {
        ConsentDecision original = Decision(ConsentCategory.Preferences, ConsentCategory.Statistics);

        ConsentDecision? decoded = ConsentCookieCodec.Decode(ConsentCookieCodec.Encode(original));

        Assert.NotNull(decoded);
        Assert.Equal(original.PolicyVersion, decoded.PolicyVersion);
        Assert.Equal(original.DecidedAt, decoded.DecidedAt);
        Assert.Equal(original.ConsentId, decoded.ConsentId);
        Assert.Equal(
            new[] { ConsentCategory.Preferences, ConsentCategory.Statistics }.ToHashSet(),
            decoded.Granted.ToHashSet());
    }

    [Fact]
    public void Omits_necessary_from_the_wire_format()
    {
        var encoded = ConsentCookieCodec.Encode(Decision(ConsentCategory.Necessary, ConsentCategory.Marketing));

        Assert.DoesNotContain("necessary", Uri.UnescapeDataString(encoded));
        Assert.Contains("marketing", Uri.UnescapeDataString(encoded));
    }

    [Fact]
    public void Necessary_is_always_granted_even_when_absent()
    {
        ConsentDecision? decoded = ConsentCookieCodec.Decode(ConsentCookieCodec.Encode(Decision()));

        Assert.NotNull(decoded);
        Assert.True(decoded.HasGranted(ConsentCategory.Necessary));
        Assert.False(decoded.HasGranted(ConsentCategory.Statistics));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("%7B%22v%22%3A")]
    [InlineData("%7B%7D")]
    public void Returns_null_for_unusable_input(string? value)
        => Assert.Null(ConsentCookieCodec.Decode(value));

    [Fact]
    public void Ignores_unknown_categories()
    {
        var json = Uri.EscapeDataString(
            """{"v":1,"t":"2026-08-21T09:12:33+00:00","c":["statistics","telepathy"],"id":"abc123"}""");

        ConsentDecision? decoded = ConsentCookieCodec.Decode(json);

        Assert.NotNull(decoded);
        Assert.Equal([ConsentCategory.Statistics], decoded.Granted.ToArray());
    }

    [Fact]
    public void Needs_reprompt_only_when_stored_version_is_older()
    {
        ConsentDecision decision = Decision();

        Assert.False(decision.NeedsRePrompt(1));
        Assert.True(decision.NeedsRePrompt(2));
    }

    [Fact]
    public void New_consent_id_is_url_safe_and_unique()
    {
        var first = ConsentCookieCodec.NewConsentId();
        var second = ConsentCookieCodec.NewConsentId();

        Assert.NotEqual(first, second);
        Assert.Equal(22, first.Length);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('=', first);
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: compile failure — `ConsentCookieCodec` and `ConsentDecision` do not exist.

- [ ] **Step 6: Write the model types**

`Consent/ConsentCategory.cs`:

```csharp
namespace NDSTK.Consent;

/// <summary>
/// The four consent categories. <see cref="Necessary"/> is never declinable and is implied rather
/// than stored, so it must not appear in the cookie's category list.
/// </summary>
public enum ConsentCategory
{
    Necessary,
    Preferences,
    Statistics,
    Marketing,
}
```

`Consent/ConsentAction.cs`:

```csharp
namespace NDSTK.Consent;

/// <summary>How a decision was reached. Recorded verbatim in the consent log (build-order stage 7).</summary>
public enum ConsentAction
{
    AcceptAll,
    RejectAll,
    Custom,
    Withdrawn,
}
```

`Consent/ConsentCategories.cs`:

```csharp
namespace NDSTK.Consent;

/// <summary>
/// Wire names for <see cref="ConsentCategory"/>. Kept as an explicit map rather than
/// <c>Enum.ToString</c> so that renaming a member cannot silently invalidate every cookie already
/// in the wild.
/// </summary>
public static class ConsentCategories
{
    /// <summary>The categories a visitor can actually choose, in banner display order.</summary>
    public static readonly IReadOnlyList<ConsentCategory> Consentable =
    [
        ConsentCategory.Preferences,
        ConsentCategory.Statistics,
        ConsentCategory.Marketing,
    ];

    /// <summary>All categories in policy-page display order, necessary first.</summary>
    public static readonly IReadOnlyList<ConsentCategory> All =
    [
        ConsentCategory.Necessary,
        ConsentCategory.Preferences,
        ConsentCategory.Statistics,
        ConsentCategory.Marketing,
    ];

    public static string ToWireName(ConsentCategory category) => category switch
    {
        ConsentCategory.Necessary => "necessary",
        ConsentCategory.Preferences => "preferences",
        ConsentCategory.Statistics => "statistics",
        ConsentCategory.Marketing => "marketing",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    public static bool TryParse(string? wireName, out ConsentCategory category)
    {
        switch (wireName)
        {
            case "necessary": category = ConsentCategory.Necessary; return true;
            case "preferences": category = ConsentCategory.Preferences; return true;
            case "statistics": category = ConsentCategory.Statistics; return true;
            case "marketing": category = ConsentCategory.Marketing; return true;
            default: category = default; return false;
        }
    }
}
```

`Consent/ConsentDecision.cs`:

```csharp
namespace NDSTK.Consent;

/// <summary>A visitor's recorded consent choice, as carried by the <c>ndstk-consent</c> cookie.</summary>
public sealed record ConsentDecision(
    int PolicyVersion,
    DateTimeOffset DecidedAt,
    IReadOnlySet<ConsentCategory> Granted,
    string ConsentId)
{
    public bool HasGranted(ConsentCategory category)
        => category == ConsentCategory.Necessary || Granted.Contains(category);

    /// <summary>
    /// True when the visitor last decided against an older version of the cookie text, which means
    /// the banner must be shown again with their previous choice pre-selected.
    /// </summary>
    public bool NeedsRePrompt(int currentPolicyVersion) => PolicyVersion < currentPolicyVersion;
}
```

- [ ] **Step 7: Write the codec**

`Consent/ConsentCookieCodec.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NDSTK.Consent;

/// <summary>
/// Serialises a <see cref="ConsentDecision"/> to and from the cookie's compact JSON form.
/// </summary>
/// <remarks>
/// Decoding is deliberately total: any malformed, truncated or hand-edited value decodes to
/// <c>null</c>, which the rest of the system treats as "no decision yet". The cookie is not a
/// security boundary — the worst a visitor can do is forge their own consent — so it is not signed.
/// </remarks>
public static class ConsentCookieCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Encode(ConsentDecision decision)
    {
        var dto = new ConsentCookieDto
        {
            Version = decision.PolicyVersion,
            DecidedAt = decision.DecidedAt.ToUniversalTime(),
            Categories = decision.Granted
                .Where(category => category != ConsentCategory.Necessary)
                .Select(ConsentCategories.ToWireName)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ConsentId = decision.ConsentId,
        };

        return Uri.EscapeDataString(JsonSerializer.Serialize(dto, SerializerOptions));
    }

    public static ConsentDecision? Decode(string? cookieValue)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
        {
            return null;
        }

        try
        {
            var json = Uri.UnescapeDataString(cookieValue);
            ConsentCookieDto? dto = JsonSerializer.Deserialize<ConsentCookieDto>(json, SerializerOptions);

            if (dto is null || dto.Version <= 0 || string.IsNullOrWhiteSpace(dto.ConsentId))
            {
                return null;
            }

            var granted = new HashSet<ConsentCategory>();
            foreach (var name in dto.Categories ?? [])
            {
                if (ConsentCategories.TryParse(name, out ConsentCategory category)
                    && category != ConsentCategory.Necessary)
                {
                    granted.Add(category);
                }
            }

            return new ConsentDecision(dto.Version, dto.DecidedAt, granted, dto.ConsentId);
        }
        catch (Exception exception) when (exception is JsonException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A random 128-bit, URL-safe id linking the cookie to its consent-log row.</summary>
    public static string NewConsentId()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class ConsentCookieDto
    {
        [JsonPropertyName("v")] public int Version { get; set; }

        [JsonPropertyName("t")] public DateTimeOffset DecidedAt { get; set; }

        [JsonPropertyName("c")] public string[]? Categories { get; set; }

        [JsonPropertyName("id")] public string? ConsentId { get; set; }
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS, 12 tests (the `[Theory]` contributes 6).

- [ ] **Step 9: Commit**

```bash
git add tests/NDSTK.Tests NDSTK.slnx Consent
git commit -m "feat(consent): add consent model and cookie codec"
```

---

### Task 2: Consent options and request-scoped consent state

**Files:**
- Create: `Consent/ConsentOptions.cs`
- Create: `Consent/IConsentState.cs`
- Create: `Consent/ConsentState.cs`
- Create: `Consent/ConsentComposer.cs`
- Test: `tests/NDSTK.Tests/Consent/ConsentStateTests.cs`

**Interfaces:**
- Consumes: `ConsentCookieCodec`, `ConsentDecision`, `ConsentCategory` from Task 1.
- Produces: `sealed class ConsentOptions` with `const string SectionName = "Ndstk:Consent"`, `int PolicyVersion`, `string CookieName`, `int CookieLifetimeDays`, `string? GoogleMeasurementId`; `interface IConsentState` with `ConsentDecision? Decision`, `bool NeedsDecision`, `bool HasGranted(ConsentCategory)`; `sealed class ConsentComposer : IComposer`.

- [ ] **Step 1: Write the failing tests**

`tests/NDSTK.Tests/Consent/ConsentStateTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

public class ConsentStateTests
{
    private static IConsentState StateFor(string? cookieValue, int policyVersion = 1)
    {
        var options = new ConsentOptions { PolicyVersion = policyVersion };
        var httpContext = new DefaultHttpContext();

        if (cookieValue is not null)
        {
            httpContext.Request.Headers.Cookie = $"{options.CookieName}={cookieValue}";
        }

        return new ConsentState(
            new HttpContextAccessor { HttpContext = httpContext },
            Options.Create(options));
    }

    private static string CookieFor(int version, params ConsentCategory[] granted)
        => ConsentCookieCodec.Encode(
            new ConsentDecision(version, DateTimeOffset.UtcNow, granted.ToHashSet(), "abc123"));

    [Fact]
    public void Needs_a_decision_when_no_cookie_is_present()
    {
        IConsentState state = StateFor(null);

        Assert.True(state.NeedsDecision);
        Assert.Null(state.Decision);
    }

    [Fact]
    public void Necessary_is_granted_even_without_a_decision()
        => Assert.True(StateFor(null).HasGranted(ConsentCategory.Necessary));

    [Fact]
    public void Non_necessary_is_denied_without_a_decision()
    {
        IConsentState state = StateFor(null);

        Assert.False(state.HasGranted(ConsentCategory.Statistics));
        Assert.False(state.HasGranted(ConsentCategory.Marketing));
        Assert.False(state.HasGranted(ConsentCategory.Preferences));
    }

    [Fact]
    public void Reads_granted_categories_from_the_cookie()
    {
        IConsentState state = StateFor(CookieFor(1, ConsentCategory.Statistics));

        Assert.False(state.NeedsDecision);
        Assert.True(state.HasGranted(ConsentCategory.Statistics));
        Assert.False(state.HasGranted(ConsentCategory.Marketing));
    }

    [Fact]
    public void An_outdated_policy_version_denies_everything_and_reprompts()
    {
        IConsentState state = StateFor(CookieFor(1, ConsentCategory.Statistics), policyVersion: 2);

        Assert.True(state.NeedsDecision);
        Assert.False(state.HasGranted(ConsentCategory.Statistics));
        Assert.True(state.HasGranted(ConsentCategory.Necessary));
        Assert.NotNull(state.Decision);
    }

    [Fact]
    public void A_corrupt_cookie_is_treated_as_no_decision()
    {
        IConsentState state = StateFor("garbage");

        Assert.True(state.NeedsDecision);
        Assert.False(state.HasGranted(ConsentCategory.Statistics));
    }

    [Fact]
    public void Survives_having_no_http_context()
    {
        IConsentState state = new ConsentState(
            new HttpContextAccessor { HttpContext = null },
            Options.Create(new ConsentOptions()));

        Assert.True(state.NeedsDecision);
        Assert.False(state.HasGranted(ConsentCategory.Statistics));
    }
}
```

The outdated-version test encodes the most important behaviour in the whole feature: a stale decision must **deny**, not carry forward. Getting this backwards would keep firing scripts the visitor has not agreed to under the current text.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: compile failure — `ConsentOptions`, `IConsentState` and `ConsentState` do not exist.

- [ ] **Step 3: Write the options**

`Consent/ConsentOptions.cs`:

```csharp
namespace NDSTK.Consent;

/// <summary>
/// Bound from the <c>Ndstk:Consent</c> configuration section.
/// </summary>
public sealed class ConsentOptions
{
    public const string SectionName = "Ndstk:Consent";

    /// <summary>
    /// Version of the cookie text. Bumping this re-prompts every visitor, so it is configuration
    /// rather than a constant: changing the policy wording is a deploy-time decision, not a code change.
    /// </summary>
    public int PolicyVersion { get; set; } = 1;

    public string CookieName { get; set; } = "ndstk-consent";

    public int CookieLifetimeDays { get; set; } = 365;

    /// <summary>
    /// Google measurement id. When null — the current state of this site — no Consent Mode snippet is
    /// emitted at all, rather than shipping dead script to every page.
    /// </summary>
    public string? GoogleMeasurementId { get; set; }
}
```

- [ ] **Step 4: Write the state service**

`Consent/IConsentState.cs`:

```csharp
namespace NDSTK.Consent;

/// <summary>Request-scoped view of the current visitor's consent.</summary>
public interface IConsentState
{
    /// <summary>The decoded decision, or null when there is no usable cookie.</summary>
    ConsentDecision? Decision { get; }

    /// <summary>True when the banner must be shown: no decision, or one made against older text.</summary>
    bool NeedsDecision { get; }

    /// <summary>
    /// True only when the visitor has actively granted this category under the current policy version.
    /// <see cref="ConsentCategory.Necessary"/> is always true.
    /// </summary>
    bool HasGranted(ConsentCategory category);
}
```

`Consent/ConsentState.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace NDSTK.Consent;

/// <summary>
/// Reads and caches the consent cookie for the lifetime of one request. Registered scoped, so the
/// cookie is parsed at most once per request no matter how many tag helpers ask.
/// </summary>
internal sealed class ConsentState(
    IHttpContextAccessor httpContextAccessor,
    IOptions<ConsentOptions> options) : IConsentState
{
    private bool _resolved;
    private ConsentDecision? _decision;

    public ConsentDecision? Decision
    {
        get
        {
            if (_resolved)
            {
                return _decision;
            }

            _resolved = true;
            var raw = httpContextAccessor.HttpContext?.Request.Cookies[options.Value.CookieName];
            _decision = ConsentCookieCodec.Decode(raw);
            return _decision;
        }
    }

    public bool NeedsDecision
        => Decision is null || Decision.NeedsRePrompt(options.Value.PolicyVersion);

    public bool HasGranted(ConsentCategory category)
    {
        if (category == ConsentCategory.Necessary)
        {
            return true;
        }

        // A decision made against older cookie text grants nothing until it is renewed.
        return NeedsDecision is false && Decision?.HasGranted(category) is true;
    }
}
```

`ConsentState` is `internal`, but the test project needs to construct it directly. Add to `NDSTK.csproj` inside the first `<ItemGroup>`:

```xml
<InternalsVisibleTo Include="NDSTK.Tests" />
```

- [ ] **Step 5: Register everything**

`Consent/ConsentComposer.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace NDSTK.Consent;

public sealed class ConsentComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();

        builder.Services
            .AddOptions<ConsentOptions>()
            .BindConfiguration(ConsentOptions.SectionName);

        builder.Services.AddScoped<IConsentState, ConsentState>();
    }
}
```

`BindConfiguration` resolves `IConfiguration` from DI, so this needs no reference to `builder.Config`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS, 19 tests total.

- [ ] **Step 7: Commit**

```bash
git add Consent NDSTK.csproj tests/NDSTK.Tests
git commit -m "feat(consent): add options and request-scoped consent state"
```

---

### Task 3: Consent endpoint

**Files:**
- Create: `Consent/ConsentRequest.cs`
- Create: `Consent/ConsentStateResponse.cs`
- Create: `Consent/ConsentCookieWriter.cs`
- Create: `Consent/ConsentController.cs`
- Create: `Consent/ConsentRateLimiting.cs`
- Modify: `Program.cs`
- Test: `tests/NDSTK.Tests/Consent/ConsentControllerTests.cs`

**Interfaces:**
- Consumes: `ConsentCookieCodec`, `ConsentDecision`, `ConsentCategories`, `ConsentAction`, `ConsentOptions` from Tasks 1–2.
- Produces: `sealed class ConsentRequest { string[]? Categories; string? Action; string? Culture; }`; `sealed record ConsentStateResponse(int Version, string[] Categories, string ConsentId, string DecidedAt)`; `sealed class ConsentCookieWriter` with `ConsentDecision Write(HttpResponse response, ConsentRequest request)`; `POST /api/consent`; `static class ConsentRateLimiting` with `const string PolicyName`.

- [ ] **Step 1: Write the failing tests**

`tests/NDSTK.Tests/Consent/ConsentControllerTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

public class ConsentControllerTests
{
    private static (ConsentController Controller, DefaultHttpContext Context) Build(int policyVersion = 1)
    {
        var options = Options.Create(new ConsentOptions { PolicyVersion = policyVersion });
        var context = new DefaultHttpContext();
        var controller = new ConsentController(new ConsentCookieWriter(options))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        return (controller, context);
    }

    private static string SetCookieHeader(DefaultHttpContext context)
        => Assert.Single(context.Response.Headers.SetCookie.ToArray().Where(h => h is not null))!;

    [Fact]
    public void Accepting_sets_the_cookie_and_returns_the_state()
    {
        (ConsentController controller, DefaultHttpContext context) = Build();

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = ["statistics", "marketing"],
            Action = "accept-all",
            Culture = "sv",
        });

        ConsentStateResponse response = Assert.IsType<ConsentStateResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(1, response.Version);
        Assert.Equal(["marketing", "statistics"], response.Categories);
        Assert.NotEmpty(response.ConsentId);

        var header = SetCookieHeader(context);
        Assert.Contains("ndstk-consent=", header);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejecting_stores_no_categories()
    {
        (ConsentController controller, _) = Build();

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = [],
            Action = "reject-all",
            Culture = "sv",
        });

        ConsentStateResponse response = Assert.IsType<ConsentStateResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Empty(response.Categories);
    }

    [Fact]
    public void Unknown_categories_are_discarded_rather_than_trusted()
    {
        (ConsentController controller, _) = Build();

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = ["statistics", "telepathy", "necessary"],
            Action = "custom",
            Culture = "sv",
        });

        ConsentStateResponse response = Assert.IsType<ConsentStateResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(["statistics"], response.Categories);
    }

    [Fact]
    public void An_unknown_action_is_rejected()
    {
        (ConsentController controller, _) = Build();

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = [],
            Action = "definitely-not-an-action",
            Culture = "sv",
        });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public void The_cookie_records_the_current_policy_version()
    {
        (ConsentController controller, _) = Build(policyVersion: 7);

        ActionResult<ConsentStateResponse> result = controller.Post(new ConsentRequest
        {
            Categories = [],
            Action = "reject-all",
            Culture = "sv",
        });

        ConsentStateResponse response = Assert.IsType<ConsentStateResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(7, response.Version);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: compile failure — `ConsentController`, `ConsentCookieWriter`, `ConsentRequest`, `ConsentStateResponse` do not exist.

- [ ] **Step 3: Write the contracts**

`Consent/ConsentRequest.cs`:

```csharp
namespace NDSTK.Consent;

/// <summary>Body of <c>POST /api/consent</c>. Every field is untrusted and validated server-side.</summary>
public sealed class ConsentRequest
{
    public string[]? Categories { get; set; }

    public string? Action { get; set; }

    public string? Culture { get; set; }
}
```

`Consent/ConsentStateResponse.cs`:

```csharp
namespace NDSTK.Consent;

/// <summary>
/// Canonical consent state after a decision. The banner uses this to unblock scripts without a reload,
/// so it must reflect what the server actually stored, not what the client asked for.
/// </summary>
public sealed record ConsentStateResponse(
    int Version,
    string[] Categories,
    string ConsentId,
    string DecidedAt);
```

- [ ] **Step 4: Write the cookie writer**

`Consent/ConsentCookieWriter.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace NDSTK.Consent;

/// <summary>
/// Turns a validated request into a decision and writes the cookie.
/// </summary>
/// <remarks>
/// The cookie is written here, server-side, rather than by JavaScript. That is what guarantees the
/// attributes are correct — lifetime, SameSite, and Secure tracking the actual scheme.
/// </remarks>
public sealed class ConsentCookieWriter(IOptions<ConsentOptions> options)
{
    /// <summary>Known action names, mapped explicitly so an unrecognised value is a hard failure.</summary>
    public static bool TryParseAction(string? action, out ConsentAction parsed)
    {
        switch (action)
        {
            case "accept-all": parsed = ConsentAction.AcceptAll; return true;
            case "reject-all": parsed = ConsentAction.RejectAll; return true;
            case "custom": parsed = ConsentAction.Custom; return true;
            case "withdrawn": parsed = ConsentAction.Withdrawn; return true;
            default: parsed = default; return false;
        }
    }

    public ConsentDecision Write(HttpResponse response, ConsentRequest request)
    {
        ConsentOptions settings = options.Value;

        var granted = new HashSet<ConsentCategory>();
        foreach (var name in request.Categories ?? [])
        {
            // Necessary is implied, never client-supplied; unknown names are discarded.
            if (ConsentCategories.TryParse(name, out ConsentCategory category)
                && category != ConsentCategory.Necessary)
            {
                granted.Add(category);
            }
        }

        var decision = new ConsentDecision(
            settings.PolicyVersion,
            DateTimeOffset.UtcNow,
            granted,
            ConsentCookieCodec.NewConsentId());

        response.Cookies.Append(settings.CookieName, ConsentCookieCodec.Encode(decision), new CookieOptions
        {
            Path = "/",
            SameSite = SameSiteMode.Lax,
            HttpOnly = false, // the banner must read this to unblock scripts without a reload
            Secure = response.HttpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(settings.CookieLifetimeDays),
            IsEssential = true,
        });

        return decision;
    }
}
```

- [ ] **Step 5: Write the rate-limiting policy name and the controller**

`Consent/ConsentRateLimiting.cs`:

```csharp
namespace NDSTK.Consent;

public static class ConsentRateLimiting
{
    public const string PolicyName = "ndstk-consent";
}
```

`Consent/ConsentController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace NDSTK.Consent;

[ApiController]
[Route("api/consent")]
public sealed class ConsentController(ConsentCookieWriter cookieWriter) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(ConsentRateLimiting.PolicyName)]
    public ActionResult<ConsentStateResponse> Post([FromBody] ConsentRequest request)
    {
        if (ConsentCookieWriter.TryParseAction(request.Action, out _) is false)
        {
            return BadRequest(new { error = "Unknown consent action." });
        }

        ConsentDecision decision = cookieWriter.Write(Response, request);

        return Ok(new ConsentStateResponse(
            decision.PolicyVersion,
            decision.Granted.Select(ConsentCategories.ToWireName).Order(StringComparer.Ordinal).ToArray(),
            decision.ConsentId,
            decision.DecidedAt.ToString("O")));
    }
}
```

The parsed action is discarded for now — it is written to the consent log in build-order stage 7. It is still validated here so that a bad client surfaces immediately rather than at that later stage.

- [ ] **Step 6: Register the writer**

In `Consent/ConsentComposer.cs`, add inside `Compose`:

```csharp
builder.Services.AddSingleton<ConsentCookieWriter>();
```

- [ ] **Step 7: Wire the endpoint and rate limiter into the host**

In `Program.cs`, add after the `builder.Configuration` block and before `builder.CreateUmbracoBuilder()`:

```csharp
builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.AddPolicy(NDSTK.Consent.ConsentRateLimiting.PolicyName, httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});
```

The partition key is the caller's IP, so one visitor cannot exhaust the limit for everyone. The IP is used for partitioning only and never persisted.

Then, immediately after `app.UseHttpsRedirection();`:

```csharp
app.UseRateLimiter();
```

And after the whole `app.UseUmbraco()` chain, before `await app.RunAsync();`:

```csharp
app.MapControllers();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS, 24 tests total.

- [ ] **Step 9: Verify the endpoint against the running site**

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:13226" ./bin/Debug/net10.0/NDSTK.exe &
curl -s -D - -o /dev/null -X POST http://localhost:13226/api/consent \
  -H 'Content-Type: application/json' \
  -d '{"categories":["statistics"],"action":"custom","culture":"sv"}' | grep -iE '^HTTP/|^set-cookie'
```

Expected: `HTTP/1.1 200 OK` and one `set-cookie: ndstk-consent=…; expires=…; path=/; samesite=lax`.

If the response is `404`, `MapControllers` is not picking the controller up. Fall back to registering it inside the Umbraco endpoint block instead:

```csharp
.WithEndpoints(u =>
{
    u.UseBackOfficeEndpoints();
    u.UseWebsiteEndpoints();
    u.EndpointRouteBuilder.MapControllers();
})
```

Then stop the site: `powershell -c "Get-Process -Name NDSTK | Stop-Process -Force"`.

- [ ] **Step 10: Commit**

```bash
git add Consent Program.cs tests/NDSTK.Tests
git commit -m "feat(consent): add consent endpoint with per-IP rate limiting"
```

---

### Task 4: `consent-script` tag helper

**Files:**
- Create: `Consent/TagHelpers/ConsentScriptTagHelper.cs`
- Modify: `Views/_ViewImports.cshtml`
- Test: `tests/NDSTK.Tests/Consent/ConsentScriptTagHelperTests.cs`
- Test: `tests/NDSTK.Tests/Consent/FakeConsentState.cs`

**Interfaces:**
- Consumes: `IConsentState`, `ConsentCategory`.
- Produces: `<consent-script category="…" src="…" async>`; `FakeConsentState` test double with `FakeConsentState(params ConsentCategory[] granted)`.

- [ ] **Step 1: Write the test double**

`tests/NDSTK.Tests/Consent/FakeConsentState.cs`:

```csharp
using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

internal sealed class FakeConsentState(params ConsentCategory[] granted) : IConsentState
{
    private readonly HashSet<ConsentCategory> _granted = granted.ToHashSet();

    public ConsentDecision? Decision => new(1, DateTimeOffset.UtcNow, _granted, "test");

    public bool NeedsDecision { get; init; }

    public bool HasGranted(ConsentCategory category)
        => category == ConsentCategory.Necessary || (NeedsDecision is false && _granted.Contains(category));
}
```

- [ ] **Step 2: Write the failing tests**

`tests/NDSTK.Tests/Consent/ConsentScriptTagHelperTests.cs`:

```csharp
using Microsoft.AspNetCore.Razor.TagHelpers;
using NDSTK.Consent;
using NDSTK.Consent.TagHelpers;

namespace NDSTK.Tests.Consent;

public class ConsentScriptTagHelperTests
{
    private static TagHelperContext Context() => new(
        new TagHelperAttributeList(),
        new Dictionary<object, object>(),
        Guid.NewGuid().ToString());

    private static TagHelperOutput Output() => new(
        "consent-script",
        new TagHelperAttributeList(),
        (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

    [Fact]
    public void Emits_nothing_at_all_when_the_category_is_not_granted()
    {
        var helper = new ConsentScriptTagHelper(new FakeConsentState())
        {
            Category = ConsentCategory.Statistics,
            Src = "https://example.test/a.js",
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.True(output.IsContentModified);
        Assert.Null(output.TagName);
        Assert.Empty(output.Content.GetContent());
    }

    [Fact]
    public void Emits_a_script_tag_when_granted()
    {
        var helper = new ConsentScriptTagHelper(new FakeConsentState(ConsentCategory.Statistics))
        {
            Category = ConsentCategory.Statistics,
            Src = "https://example.test/a.js",
            Async = true,
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.Equal("script", output.TagName);
        Assert.Equal(TagMode.StartTagAndEndTag, output.TagMode);
        Assert.Equal("https://example.test/a.js", output.Attributes["src"].Value);
        Assert.True(output.Attributes.ContainsName("async"));
    }

    [Fact]
    public void Omits_async_when_not_requested()
    {
        var helper = new ConsentScriptTagHelper(new FakeConsentState(ConsentCategory.Marketing))
        {
            Category = ConsentCategory.Marketing,
            Src = "https://example.test/a.js",
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.False(output.Attributes.ContainsName("async"));
    }

    [Fact]
    public void Necessary_scripts_are_always_emitted()
    {
        var helper = new ConsentScriptTagHelper(new FakeConsentState())
        {
            Category = ConsentCategory.Necessary,
            Src = "/static/js/consent.js",
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.Equal("script", output.TagName);
    }

    [Fact]
    public void A_stale_decision_suppresses_the_script()
    {
        var helper = new ConsentScriptTagHelper(
            new FakeConsentState(ConsentCategory.Statistics) { NeedsDecision = true })
        {
            Category = ConsentCategory.Statistics,
            Src = "https://example.test/a.js",
        };
        TagHelperOutput output = Output();

        helper.Process(Context(), output);

        Assert.Null(output.TagName);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: compile failure — `ConsentScriptTagHelper` does not exist.

- [ ] **Step 4: Write the tag helper**

`Consent/TagHelpers/ConsentScriptTagHelper.cs`:

```csharp
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace NDSTK.Consent.TagHelpers;

/// <summary>
/// Emits a <c>&lt;script&gt;</c> only when the visitor has granted the given category.
/// </summary>
/// <remarks>
/// This is the primary gating mechanism and the reason the "no consenting cookies before a choice"
/// guarantee holds without a race: when consent is absent the tag never reaches the browser at all,
/// so there is no window in which it could execute.
/// </remarks>
[HtmlTargetElement("consent-script")]
public sealed class ConsentScriptTagHelper(IConsentState consent) : TagHelper
{
    [HtmlAttributeName("category")]
    public ConsentCategory Category { get; set; } = ConsentCategory.Marketing;

    [HtmlAttributeName("src")]
    public string? Src { get; set; }

    [HtmlAttributeName("async")]
    public bool Async { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (consent.HasGranted(Category) is false)
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;

        if (string.IsNullOrWhiteSpace(Src) is false)
        {
            output.Attributes.SetAttribute("src", Src);
        }

        if (Async)
        {
            output.Attributes.SetAttribute(
                new TagHelperAttribute("async", null, HtmlAttributeValueStyle.Minimized));
        }
    }
}
```

The default category is `Marketing` rather than `Necessary`: forgetting the attribute should fail closed.

- [ ] **Step 5: Register tag helpers in views**

Append to `Views/_ViewImports.cshtml`:

```razor
@using NDSTK.Consent
@addTagHelper *, NDSTK
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS, 29 tests total.

- [ ] **Step 7: Commit**

```bash
git add Consent/TagHelpers Views/_ViewImports.cshtml tests/NDSTK.Tests
git commit -m "feat(consent): add consent-script tag helper"
```

---

### Task 5: `consent-embed` tag helper

**Files:**
- Create: `Consent/TagHelpers/ConsentEmbedTagHelper.cs`
- Test: `tests/NDSTK.Tests/Consent/ConsentEmbedTagHelperTests.cs`

**Interfaces:**
- Consumes: `IConsentState`, `ConsentCategory`, `ConsentCategories`, `ICultureDictionaryFactory`.
- Produces: `<consent-embed category="…" src="…" title="…" />`.

- [ ] **Step 1: Write the failing tests**

`tests/NDSTK.Tests/Consent/ConsentEmbedTagHelperTests.cs`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using NDSTK.Consent;
using NDSTK.Consent.TagHelpers;
using Umbraco.Cms.Core.Dictionary;

namespace NDSTK.Tests.Consent;

public class ConsentEmbedTagHelperTests
{
    private sealed class StubDictionary : ICultureDictionary, ICultureDictionaryFactory
    {
        public string this[string key] => $"[{key}]";

        public CultureInfo Culture => CultureInfo.InvariantCulture;

        public IDictionary<string, string> GetChildren(string key) => new Dictionary<string, string>();

        public ICultureDictionary CreateDictionary() => this;

        public ICultureDictionary CreateDictionary(CultureInfo culture) => this;
    }

    private static TagHelperContext Context() => new(
        new TagHelperAttributeList(),
        new Dictionary<object, object>(),
        Guid.NewGuid().ToString());

    private static TagHelperOutput Output() => new(
        "consent-embed",
        new TagHelperAttributeList(),
        (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

    private static ConsentEmbedTagHelper Helper(IConsentState consent) =>
        new(consent, new StubDictionary())
        {
            Category = ConsentCategory.Marketing,
            Src = "https://www.youtube-nocookie.com/embed/abc",
            Title = "Klubbfilm",
        };

    [Fact]
    public void Renders_an_iframe_when_granted()
    {
        TagHelperOutput output = Output();

        Helper(new FakeConsentState(ConsentCategory.Marketing)).Process(Context(), output);

        var html = output.Content.GetContent();
        Assert.Equal("div", output.TagName);
        Assert.Contains("<iframe", html);
        Assert.Contains("https://www.youtube-nocookie.com/embed/abc", html);
        Assert.Contains("title=\"Klubbfilm\"", html);
    }

    [Fact]
    public void Renders_a_placeholder_with_no_iframe_when_not_granted()
    {
        TagHelperOutput output = Output();

        Helper(new FakeConsentState()).Process(Context(), output);

        var html = output.Content.GetContent();
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("data-consent-open", html);
        Assert.Contains("[Cookies.Embed.Blocked.Body]", html);
        Assert.Contains("[Cookies.Embed.Blocked.Button]", html);
    }

    [Fact]
    public void The_placeholder_never_leaks_the_embed_url()
    {
        TagHelperOutput output = Output();

        Helper(new FakeConsentState()).Process(Context(), output);

        Assert.DoesNotContain("youtube-nocookie.com", output.Content.GetContent());
    }

    [Fact]
    public void Escapes_a_hostile_title()
    {
        TagHelperOutput output = Output();
        ConsentEmbedTagHelper helper = Helper(new FakeConsentState(ConsentCategory.Marketing));
        helper.Title = "\"><script>alert(1)</script>";

        helper.Process(Context(), output);

        Assert.DoesNotContain("<script>alert(1)</script>", output.Content.GetContent());
    }
}
```

The URL-leak test matters: a placeholder that still contains the third-party URL invites a future change to "just render the iframe hidden", which would fire the request anyway.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: compile failure — `ConsentEmbedTagHelper` does not exist.

- [ ] **Step 3: Write the tag helper**

`Consent/TagHelpers/ConsentEmbedTagHelper.cs`:

```csharp
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Umbraco.Cms.Core.Dictionary;

namespace NDSTK.Consent.TagHelpers;

/// <summary>
/// Renders a third-party embed, or a placeholder inviting the visitor to grant the category it needs.
/// </summary>
/// <remarks>
/// The placeholder deliberately does not contain the embed URL in any form. Emitting it — even hidden,
/// even in a data attribute — is how "blocked" embeds end up firing requests anyway.
/// </remarks>
[HtmlTargetElement("consent-embed", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ConsentEmbedTagHelper(
    IConsentState consent,
    ICultureDictionaryFactory cultureDictionaryFactory) : TagHelper
{
    [HtmlAttributeName("category")]
    public ConsentCategory Category { get; set; } = ConsentCategory.Marketing;

    [HtmlAttributeName("src")]
    public string? Src { get; set; }

    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        HtmlEncoder encoder = HtmlEncoder.Default;
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        if (consent.HasGranted(Category))
        {
            output.Attributes.SetAttribute("class", "consent-embed");
            output.Content.SetHtmlContent(
                $"""<iframe src="{encoder.Encode(Src ?? string.Empty)}" title="{encoder.Encode(Title ?? string.Empty)}" loading="lazy" allowfullscreen></iframe>""");
            return;
        }

        ICultureDictionary dictionary = cultureDictionaryFactory.CreateDictionary();
        var body = dictionary["Cookies.Embed.Blocked.Body"];
        var button = dictionary["Cookies.Embed.Blocked.Button"];

        output.Attributes.SetAttribute("class", "consent-embed consent-embed--blocked");
        output.Attributes.SetAttribute("data-consent-category", ConsentCategories.ToWireName(Category));
        output.Content.SetHtmlContent(
            $"""
            <p>{encoder.Encode(body)}</p>
            <button type="button" class="btn-primary" data-consent-open>{encoder.Encode(button)}</button>
            """);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS, 33 tests total.

- [ ] **Step 5: Commit**

```bash
git add Consent/TagHelpers tests/NDSTK.Tests
git commit -m "feat(consent): add consent-embed tag helper with blocking placeholder"
```

---

### Task 6: Google Consent Mode v2 snippets

**Files:**
- Create: `Consent/ConsentModeScript.cs`
- Test: `tests/NDSTK.Tests/Consent/ConsentModeScriptTests.cs`

**Interfaces:**
- Consumes: `IConsentState`, `ConsentCategory`.
- Produces: `static class ConsentModeScript` with `string Defaults()` and `string Update(IConsentState)`.

- [ ] **Step 1: Write the failing tests**

`tests/NDSTK.Tests/Consent/ConsentModeScriptTests.cs`:

```csharp
using NDSTK.Consent;

namespace NDSTK.Tests.Consent;

public class ConsentModeScriptTests
{
    [Fact]
    public void Defaults_deny_every_signal()
    {
        var script = ConsentModeScript.Defaults();

        Assert.Contains("'ad_storage':'denied'", script);
        Assert.Contains("'ad_user_data':'denied'", script);
        Assert.Contains("'ad_personalization':'denied'", script);
        Assert.Contains("'analytics_storage':'denied'", script);
        Assert.Contains("'functionality_storage':'denied'", script);
        Assert.Contains("'personalization_storage':'denied'", script);
        Assert.Contains("'wait_for_update':500", script);
        Assert.DoesNotContain("granted", script);
    }

    [Fact]
    public void Statistics_grants_only_analytics_storage()
    {
        var script = ConsentModeScript.Update(new FakeConsentState(ConsentCategory.Statistics));

        Assert.Contains("'analytics_storage':'granted'", script);
        Assert.Contains("'ad_storage':'denied'", script);
        Assert.Contains("'functionality_storage':'denied'", script);
    }

    [Fact]
    public void Marketing_grants_the_three_ad_signals()
    {
        var script = ConsentModeScript.Update(new FakeConsentState(ConsentCategory.Marketing));

        Assert.Contains("'ad_storage':'granted'", script);
        Assert.Contains("'ad_user_data':'granted'", script);
        Assert.Contains("'ad_personalization':'granted'", script);
        Assert.Contains("'analytics_storage':'denied'", script);
    }

    [Fact]
    public void Preferences_grants_functionality_and_personalization()
    {
        var script = ConsentModeScript.Update(new FakeConsentState(ConsentCategory.Preferences));

        Assert.Contains("'functionality_storage':'granted'", script);
        Assert.Contains("'personalization_storage':'granted'", script);
        Assert.Contains("'ad_storage':'denied'", script);
    }

    [Fact]
    public void Nothing_granted_denies_everything()
    {
        var script = ConsentModeScript.Update(new FakeConsentState());

        Assert.DoesNotContain("granted", script);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: compile failure — `ConsentModeScript` does not exist.

- [ ] **Step 3: Write the snippet builder**

`Consent/ConsentModeScript.cs`:

```csharp
using System.Text;

namespace NDSTK.Consent;

/// <summary>
/// Builds the Google Consent Mode v2 <c>default</c> and <c>update</c> calls.
/// </summary>
/// <remarks>
/// The default call must run before any Google tag loads, which is why it is emitted inline in
/// <c>&lt;head&gt;</c> rather than from <c>consent.js</c>. Emitted only when a measurement id is
/// configured — see <see cref="ConsentOptions.GoogleMeasurementId"/>.
/// </remarks>
public static class ConsentModeScript
{
    private const string Preamble =
        "window.dataLayer=window.dataLayer||[];function gtag(){dataLayer.push(arguments);}";

    public static string Defaults() =>
        Preamble +
        "gtag('consent','default',{" +
        "'ad_storage':'denied'," +
        "'ad_user_data':'denied'," +
        "'ad_personalization':'denied'," +
        "'analytics_storage':'denied'," +
        "'functionality_storage':'denied'," +
        "'personalization_storage':'denied'," +
        "'wait_for_update':500});";

    public static string Update(IConsentState consent)
    {
        var marketing = Signal(consent.HasGranted(ConsentCategory.Marketing));
        var statistics = Signal(consent.HasGranted(ConsentCategory.Statistics));
        var preferences = Signal(consent.HasGranted(ConsentCategory.Preferences));

        return new StringBuilder()
            .Append("gtag('consent','update',{")
            .Append($"'ad_storage':'{marketing}',")
            .Append($"'ad_user_data':'{marketing}',")
            .Append($"'ad_personalization':'{marketing}',")
            .Append($"'analytics_storage':'{statistics}',")
            .Append($"'functionality_storage':'{preferences}',")
            .Append($"'personalization_storage':'{preferences}'")
            .Append("});")
            .ToString();
    }

    private static string Signal(bool granted) => granted ? "granted" : "denied";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/NDSTK.Tests/NDSTK.Tests.csproj`
Expected: PASS, 38 tests total.

- [ ] **Step 5: Commit**

```bash
git add Consent tests/NDSTK.Tests
git commit -m "feat(consent): add Google Consent Mode v2 snippet builder"
```

---

### Task 7: Cookie registry content model

**Files:**
- Modify: `ContentModel/NdstkKeys.cs`
- Modify: `ContentModel/NdstkContentModelInstaller.cs`
- Create: `Views/CookiePolicy.cshtml` (stub, filled in Task 10)

**Interfaces:**
- Consumes: `NdstkContentTypeFactory`, `NdstkKeys` from the existing installer.
- Produces: element type alias `cookieDefinition`; document type alias `cookiePolicy` with properties `heading`, `introduction`, `cookies`, `outro`; data types `NDSTK - Cookie category`, `NDSTK - Storage type`, `NDSTK - Cookie registry`; Settings property `cookiePolicyPage`; template alias `CookiePolicy`.

- [ ] **Step 1: Add the keys**

In `ContentModel/NdstkKeys.cs`, add to `Templates`:

```csharp
internal static readonly Guid CookiePolicy = new("85504a4c-d7d4-4dc2-89ad-d5b3de6016c0");
```

Add to `DocumentTypes`:

```csharp
internal static readonly Guid CookiePolicy = new("cc364eaf-944c-4964-8a06-61927c5b3a30");
```

Add to `ElementTypes`:

```csharp
internal static readonly Guid CookieDefinition = new("e1e50001-0000-4000-8000-000000000008");
```

Add to `DataTypes`:

```csharp
internal static readonly Guid CookieCategory = new("da7a0001-0000-4000-8000-000000000005");
internal static readonly Guid StorageType = new("da7a0001-0000-4000-8000-000000000006");
internal static readonly Guid CookieRegistry = new("da7a0001-0000-4000-8000-000000000007");
```

Add to `Nodes`:

```csharp
internal static readonly Guid CookiePolicy = new("c0117e17-0000-4000-8000-000000000006");
```

- [ ] **Step 2: Create the template stub so the installer can read it**

`Views/CookiePolicy.cshtml`:

```razor
@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage
@{
    Layout = "Root.cshtml";
}
```

The installer reads the physical file and hands its content to Umbraco, so the file must exist before the template is registered. Task 10 fills it in.

- [ ] **Step 3: Register the template**

In `ContentModel/NdstkContentModelInstaller.cs`, add to the `definitions` array in `InstallTemplatesAsync`:

```csharp
(Templates.CookiePolicy, "Cookie policy", "CookiePolicy"),
```

- [ ] **Step 4: Add the two dropdown data types**

In `InstallDataTypesAsync`, add:

```csharp
await factory.EnsureDataTypeAsync(
    DataTypes.CookieCategory,
    "NDSTK - Cookie category",
    Constants.PropertyEditors.Aliases.DropDownListFlexible,
    "Umb.PropertyEditorUi.Dropdown",
    new Dictionary<string, object>
    {
        ["multiple"] = false,
        ["items"] = new[] { "necessary", "preferences", "statistics", "marketing" },
    });

await factory.EnsureDataTypeAsync(
    DataTypes.StorageType,
    "NDSTK - Storage type",
    Constants.PropertyEditors.Aliases.DropDownListFlexible,
    "Umb.PropertyEditorUi.Dropdown",
    new Dictionary<string, object>
    {
        ["multiple"] = false,
        ["items"] = new[] { "Cookie", "localStorage", "sessionStorage", "Pixel" },
    });
```

Category values are the wire names from `ConsentCategories.ToWireName`, not display labels — the policy page groups by them, so they must match exactly. Display names come from Dictionary.

- [ ] **Step 5: Add the element type**

In `InstallElementTypesAsync`, add:

```csharp
await EnsureElementTypeAsync(ElementTypes.CookieDefinition, "cookieDefinition", "Cookie", "icon-lock",
    "One declared cookie, shown in the cookie policy table.",
    factory.Property(BuiltInDataTypes.Textstring, "cookieName", "Name", "Literal name or pattern, e.g. _ga_*", 0),
    factory.Property(BuiltInDataTypes.Textstring, "provider", "Provider", "NDSTK, Google, YouTube…", 1),
    factory.Property(DataTypes.CookieCategory, "category", "Category", sortOrder: 2),
    factory.Property(BuiltInDataTypes.Textarea, "purpose", "Purpose", sortOrder: 3),
    factory.Property(BuiltInDataTypes.Textstring, "duration", "Duration", "\"12 månader\", \"Session\"", 4),
    factory.Property(DataTypes.StorageType, "storageType", "Storage type", sortOrder: 5));
```

`EnsureElementTypeAsync` must be called after `PreloadDataTypesAsync` has loaded `DataTypes.CookieCategory` and `DataTypes.StorageType`. Since element types are installed before data types in the current ordering, move these two `EnsureDataTypeAsync` calls into a new method `InstallCookieDataTypesAsync()` invoked **before** `InstallElementTypesAsync()`, and add both keys to a `PreloadDataTypesAsync` call at that point. The Block List in Step 6 still belongs in `InstallDataTypesAsync`, because it references the element type.

- [ ] **Step 6: Add the registry Block List**

In `InstallDataTypesAsync`, add:

```csharp
await factory.EnsureDataTypeAsync(
    DataTypes.CookieRegistry,
    "NDSTK - Cookie registry",
    Constants.PropertyEditors.Aliases.BlockList,
    "Umb.PropertyEditorUi.BlockList",
    new Dictionary<string, object>
    {
        ["blocks"] = new object[] { Block(ElementTypes.CookieDefinition, "Cookie") },
    });
```

- [ ] **Step 7: Add the document type**

In `InstallDocumentTypesAsync`, add `DataTypes.CookieRegistry` to the `PreloadDataTypesAsync` call, then add:

```csharp
await factory.EnsureContentTypeAsync(
    DocumentTypes.CookiePolicy, "cookiePolicy", "Cookie policy", "icon-lock", type =>
    {
        type.AddContentType(baseType);
        NdstkContentTypeFactory.UseTemplate(type, templates[Templates.CookiePolicy]);
        NdstkContentTypeFactory.AddGroup(type, DeriveKey(DocumentTypes.CookiePolicy, 1), "content", "Content", 0,
            factory.Property(BuiltInDataTypes.Textstring, "heading", "Heading", "Falls back to the node name.", 0),
            factory.Property(BuiltInDataTypes.RichtextEditor, "introduction", "Introduction", sortOrder: 1),
            factory.Property(DataTypes.CookieRegistry, "cookies", "Declared cookies", sortOrder: 2),
            factory.Property(BuiltInDataTypes.RichtextEditor, "outro", "Closing text", sortOrder: 3));
    });
```

- [ ] **Step 8: Allow it under Start and add the Settings picker**

Extend the existing `SetAllowedChildrenAsync` call for `DocumentTypes.Start` with a final entry:

```csharp
(DocumentTypes.CookiePolicy, "cookiePolicy"));
```

And add to the Settings property group, after `footerText`:

```csharp
factory.Property(BuiltInDataTypes.ContentPicker, "cookiePolicyPage", "Cookie policy page",
    "Linked from the consent banner and the footer.", 5));
```

Because `EnsureContentTypeAsync` is create-if-missing, the new Settings property will **not** appear on an existing database. Delete the Settings document type in the backoffice and restart, or add the property by hand. Note this in the commit message.

- [ ] **Step 9: Build, run and verify in the backoffice**

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:13226" ./bin/Debug/net10.0/NDSTK.exe &
```

Expected in the log: `NDSTK content model is up to date.` and no `Installing the NDSTK content model failed.`

Then confirm the uSync auto-export wrote the new files, which is the cheapest proof the schema persisted correctly:

```bash
ls uSync/v18/ContentTypes/cookiedefinition.config uSync/v18/ContentTypes/cookiepolicy.config
cat uSync/v18/DataTypes/NDSTKCookieRegistry.config
```

Expected: the registry config's `blocks` array contains `contentElementTypeKey` `e1e50001-0000-4000-8000-000000000008`.

Stop the site.

- [ ] **Step 10: Commit**

```bash
git add ContentModel Views/CookiePolicy.cshtml
git commit -m "feat(consent): add cookie registry element type and policy document type

The new Settings cookiePolicyPage property only appears on a fresh database;
the installer is create-if-missing and will not alter existing schema."
```

---

### Task 8: Dictionary seeding

**Files:**
- Create: `ContentModel/NdstkDictionaryInstaller.cs`
- Modify: `ContentModel/NdstkContentModelComposer.cs`
- Modify: `ContentModel/NdstkContentModelInstaller.cs`

**Interfaces:**
- Consumes: `ILanguageService`, `IDictionaryItemService`.
- Produces: `sealed class NdstkDictionaryInstaller` with `Task InstallAsync()`; Dictionary keys under the `Cookies.` prefix.

- [ ] **Step 1: Write the installer**

`ContentModel/NdstkDictionaryInstaller.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace NDSTK.ContentModel;

/// <summary>
/// Seeds the consent banner's text as Umbraco Dictionary items.
/// </summary>
/// <remarks>
/// Dictionary items are culture-variant regardless of document type variance, which is what lets the
/// banner be bilingual while the content types remain invariant.
/// </remarks>
internal sealed class NdstkDictionaryInstaller(
    IDictionaryItemService dictionaryItemService,
    ILanguageService languageService,
    ILogger<NdstkDictionaryInstaller> logger)
{
    private static readonly Guid UserKey = Constants.Security.SuperUserKey;

    /// <summary>Key, Swedish, English. Swedish first because it is the default language.</summary>
    private static readonly (string Key, string Sv, string En)[] Items =
    [
        ("Cookies.Banner.Heading", "Vi använder kakor", "We use cookies"),
        ("Cookies.Banner.Body",
            "Vi använder nödvändiga kakor för att sajten ska fungera. Vi vill också gärna använda kakor för statistik och innehåll från andra tjänster.",
            "We use necessary cookies to make the site work. We would also like to use cookies for statistics and content from other services."),
        ("Cookies.Banner.AcceptAll", "Godkänn alla", "Accept all"),
        ("Cookies.Banner.RejectAll", "Neka alla", "Reject all"),
        ("Cookies.Banner.Customise", "Anpassa", "Customise"),
        ("Cookies.Banner.Save", "Spara val", "Save choices"),
        ("Cookies.Banner.Cancel", "Avbryt", "Cancel"),
        ("Cookies.Banner.PolicyLink", "Läs mer om kakor", "Read more about cookies"),
        ("Cookies.Banner.Label", "Samtycke till kakor", "Cookie consent"),
        ("Cookies.Settings.Heading", "Inställningar för kakor", "Cookie settings"),
        ("Cookies.Category.Necessary.Name", "Nödvändiga", "Necessary"),
        ("Cookies.Category.Necessary.Description",
            "Krävs för att sajten ska fungera, till exempel inloggning. Kan inte stängas av.",
            "Required for the site to work, for example logging in. Cannot be turned off."),
        ("Cookies.Category.Preferences.Name", "Funktionella", "Preferences"),
        ("Cookies.Category.Preferences.Description",
            "Sparar dina val, till exempel språk.",
            "Remembers your choices, such as language."),
        ("Cookies.Category.Statistics.Name", "Statistik", "Statistics"),
        ("Cookies.Category.Statistics.Description",
            "Hjälper oss förstå hur sajten används. Helt anonymt.",
            "Helps us understand how the site is used. Fully anonymous."),
        ("Cookies.Category.Marketing.Name", "Marknadsföring", "Marketing"),
        ("Cookies.Category.Marketing.Description",
            "Används av inbäddat innehåll, till exempel filmer från YouTube.",
            "Used by embedded content, such as YouTube videos."),
        ("Cookies.Category.Cookies", "Kakor i den här kategorin", "Cookies in this category"),
        ("Cookies.Embed.Blocked.Body",
            "Det här innehållet kommer från en annan tjänst och kräver ditt samtycke.",
            "This content comes from another service and needs your consent."),
        ("Cookies.Embed.Blocked.Button", "Visa innehåll", "Show content"),
        ("Cookies.Policy.CurrentChoice", "Ditt nuvarande val", "Your current choice"),
        ("Cookies.Policy.NoChoice", "Du har inte gjort något val än.", "You have not made a choice yet."),
        ("Cookies.Policy.Reopen", "Ändra inställningar", "Change settings"),
        ("Cookies.Policy.Withdraw", "Återkalla samtycke", "Withdraw consent"),
        ("Cookies.Footer.Link", "Cookieinställningar", "Cookie settings"),
        ("Cookies.Table.Name", "Namn", "Name"),
        ("Cookies.Table.Provider", "Leverantör", "Provider"),
        ("Cookies.Table.Purpose", "Syfte", "Purpose"),
        ("Cookies.Table.Duration", "Lagringstid", "Duration"),
        ("Cookies.Table.Type", "Typ", "Type"),
    ];

    public async Task InstallAsync()
    {
        ILanguage? swedish = await languageService.GetAsync("sv");
        ILanguage? english = await languageService.GetAsync("en-GB");

        if (swedish is null)
        {
            logger.LogWarning("Skipping dictionary seeding: the 'sv' language does not exist yet.");
            return;
        }

        var created = 0;
        foreach ((string key, string sv, string en) in Items)
        {
            if (await dictionaryItemService.ExistsAsync(key))
            {
                continue;
            }

            var translations = new List<IDictionaryTranslation>
            {
                new DictionaryTranslation(swedish, sv),
            };

            if (english is not null)
            {
                translations.Add(new DictionaryTranslation(english, en));
            }

            var item = new DictionaryItem(key) { Translations = translations };

            var attempt = await dictionaryItemService.CreateAsync(item, UserKey);
            if (attempt.Success is false)
            {
                logger.LogWarning("Could not create dictionary item {Key}: {Status}.", key, attempt.Status);
                continue;
            }

            created++;
        }

        if (created > 0)
        {
            logger.LogInformation("Seeded {Count} cookie dictionary items.", created);
        }
    }
}
```

- [ ] **Step 2: Register and invoke it**

In `ContentModel/NdstkContentModelComposer.cs`, add:

```csharp
builder.Services.AddSingleton<NdstkDictionaryInstaller>();
```

In `ContentModel/NdstkContentModelInstaller.cs`, add `NdstkDictionaryInstaller dictionary` to the primary constructor parameters, and call it in `InstallAsync` immediately after `await languages.InstallAsync();`:

```csharp
await dictionary.InstallAsync();
```

Ordering matters: the languages must exist before the translations can reference them.

- [ ] **Step 3: Build, run and verify**

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:13226" ./bin/Debug/net10.0/NDSTK.exe &
```

Expected in the log: `Seeded 31 cookie dictionary items.` on the first run, and nothing on a second restart.

Then confirm uSync exported them:

```bash
ls uSync/v18/Dictionary/ | head
```

Stop the site.

- [ ] **Step 4: Commit**

```bash
git add ContentModel
git commit -m "feat(consent): seed bilingual cookie dictionary items"
```

---

### Task 9: Banner markup and styles

**Files:**
- Create: `Views/Partials/_ConsentBanner.cshtml`
- Create: `wwwroot/static/css/consent.css`
- Modify: `Views/Root.cshtml`

**Interfaces:**
- Consumes: `IConsentState`, `ConsentCategories`, `ConsentOptions`, `ConsentModeScript`, Dictionary keys from Task 8.
- Produces: `.consent-bar`, `#consent-dialog`, `[data-consent-open]`, `[data-consent-action]`, `[data-consent-category-input]` — the hooks Task 10's JavaScript binds to.

- [ ] **Step 1: Write the partial**

`Views/Partials/_ConsentBanner.cshtml`:

```razor
@using Microsoft.Extensions.Options
@using NDSTK.Consent
@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage
@inject IConsentState Consent
@inject IOptions<ConsentOptions> ConsentSettings

@{
    var settings = ConsentSettings.Value;
    var policyPage = Model?.Root()?.ChildrenOfType("settings").FirstOrDefault()
        ?.Value<IPublishedContent>("cookiePolicyPage");
    var granted = Consent.Decision?.Granted ?? new HashSet<ConsentCategory>();
    var showBar = Consent.NeedsDecision;
}

@if (showBar)
{
    <div class="consent-bar" role="region" aria-label="@(Umbraco.GetDictionaryValueOrDefault("Cookies.Banner.Label", "Samtycke till kakor"))" data-consent-bar>
        <div class="consent-bar__text">
            <h2>@(Umbraco.GetDictionaryValueOrDefault("Cookies.Banner.Heading", "Vi använder kakor"))</h2>
            <p>
                @(Umbraco.GetDictionaryValueOrDefault("Cookies.Banner.Body", string.Empty))
                @if (policyPage is not null)
                {
                    <a href="@policyPage.Url()">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Banner.PolicyLink", "Läs mer"))</a>
                }
            </p>
        </div>
        <div class="consent-bar__actions">
            <button type="button" class="btn-primary" data-consent-action="accept-all">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Banner.AcceptAll", "Godkänn alla"))</button>
            <button type="button" class="btn-secondary" data-consent-action="reject-all">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Banner.RejectAll", "Neka alla"))</button>
            <button type="button" class="btn-link" data-consent-open>@(Umbraco.GetDictionaryValueOrDefault("Cookies.Banner.Customise", "Anpassa"))</button>
        </div>
    </div>
}

<dialog id="consent-dialog" class="consent-dialog" aria-labelledby="consent-dialog-heading">
    <form method="dialog" class="consent-dialog__form" data-consent-form>
        <h2 id="consent-dialog-heading">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Settings.Heading", "Inställningar för kakor"))</h2>

        @foreach (var category in ConsentCategories.All)
        {
            var wire = ConsentCategories.ToWireName(category);
            var isNecessary = category == ConsentCategory.Necessary;
            var inputId = $"consent-cat-{wire}";
            <fieldset class="consent-category">
                <legend>@(Umbraco.GetDictionaryValueOrDefault($"Cookies.Category.{category}.Name", wire))</legend>
                <div class="consent-category__row">
                    <input type="checkbox"
                           id="@inputId"
                           value="@wire"
                           data-consent-category-input
                           checked="@(isNecessary || granted.Contains(category))"
                           disabled="@isNecessary" />
                    <label for="@inputId">
                        @(Umbraco.GetDictionaryValueOrDefault($"Cookies.Category.{category}.Description", string.Empty))
                    </label>
                </div>
            </fieldset>
        }

        <div class="consent-dialog__actions">
            <button type="button" class="btn-primary" data-consent-action="custom">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Banner.Save", "Spara val"))</button>
            <button type="button" class="btn-secondary" data-consent-close>@(Umbraco.GetDictionaryValueOrDefault("Cookies.Banner.Cancel", "Avbryt"))</button>
        </div>
    </form>
</dialog>

<script src="~/static/js/consent.js" defer
        data-consent-endpoint="/api/consent"
        data-consent-cookie="@settings.CookieName"
        data-consent-version="@settings.PolicyVersion"
        data-consent-mode="@(string.IsNullOrWhiteSpace(settings.GoogleMeasurementId) ? "off" : "on")"></script>
```

The `necessary` checkbox is `disabled` but its `<label>` carries the reason, so a screen reader announces why it cannot be changed rather than presenting an unexplained dead control.

- [ ] **Step 2: Write the styles**

Append to `wwwroot/static/css/consent.css`:

```css
/* Consent bar
   A bar rather than a blocking modal: the "no cookies before a choice" guarantee is enforced
   server-side, so blocking the page buys nothing and costs a great deal of accessibility. */
.consent-bar {
    position: fixed;
    inset: auto 0 0 0;
    z-index: 1000;
    background: white;
    border-top: 3px solid var(--accent);
    box-shadow: 0 -2px 12px rgba(0,0,0,0.15);
    padding: 1.25rem;
    display: flex;
    flex-wrap: wrap;
    gap: 1rem 2rem;
    align-items: center;
    justify-content: space-between;
    max-width: 100%;
}

.consent-bar__text {
    flex: 1 1 22rem;
}

    .consent-bar__text h2 {
        color: var(--primary);
        margin: 0 0 0.35rem;
        font-size: 1.15rem;
    }

    .consent-bar__text p {
        margin: 0;
        font-size: 0.95rem;
    }

.consent-bar__actions {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    align-items: center;
}

/* Accept and reject must be visually equal. Same box, different colour - nothing else. */
.btn-secondary {
    display: inline-block;
    background: var(--primary);
    color: white;
    padding: 0.6rem 1.2rem;
    border: none;
    border-radius: 4px;
    font-weight: bold;
    font-size: 1rem;
    font-family: inherit;
    cursor: pointer;
    margin-top: 1rem;
}

    .btn-secondary:hover {
        text-decoration: underline;
    }

.consent-bar .btn-primary,
.consent-bar .btn-secondary,
.consent-dialog .btn-primary,
.consent-dialog .btn-secondary {
    margin-top: 0;
    border: none;
    font-size: 1rem;
    font-family: inherit;
    cursor: pointer;
}

.btn-link {
    background: none;
    border: none;
    padding: 0.6rem 0.4rem;
    color: var(--primary);
    font: inherit;
    text-decoration: underline;
    cursor: pointer;
}

/* Settings dialog - native <dialog>, so focus trap, Esc and the inert backdrop come from the platform */
.consent-dialog {
    border: none;
    border-radius: 8px;
    padding: 0;
    max-width: 34rem;
    width: calc(100% - 2rem);
    color: var(--text);
    background: white;
}

.consent-dialog::backdrop {
    background: rgba(0, 31, 84, 0.6);
}

.consent-dialog__form {
    padding: 1.5rem;
}

    .consent-dialog__form h2 {
        color: var(--primary);
        margin: 0 0 1rem;
        border-bottom: 2px solid var(--accent);
        padding-bottom: 0.5rem;
    }

.consent-category {
    border: 1px solid #d5d7db;
    border-radius: 6px;
    margin: 0 0 1rem;
    padding: 0.75rem 1rem 1rem;
}

    .consent-category legend {
        color: var(--primary);
        font-weight: 700;
        padding: 0 0.35rem;
    }

.consent-category__row {
    display: flex;
    gap: 0.75rem;
    align-items: flex-start;
    font-size: 0.95rem;
}

    .consent-category__row input {
        margin-top: 0.2rem;
        width: 1.15rem;
        height: 1.15rem;
        flex: 0 0 auto;
    }

.consent-dialog__actions {
    display: flex;
    gap: 0.75rem;
    flex-wrap: wrap;
    margin-top: 1.25rem;
}

/* Visible focus everywhere - this is a keyboard-driven component by design */
.consent-bar :focus-visible,
.consent-dialog :focus-visible,
.consent-embed :focus-visible {
    outline: 3px solid var(--primary);
    outline-offset: 2px;
}

/* Blocked embed placeholder */
.consent-embed--blocked {
    background: var(--bg);
    border: 1px dashed var(--primary);
    border-radius: 6px;
    padding: 1.5rem;
    text-align: center;
}

.consent-embed iframe {
    width: 100%;
    aspect-ratio: 16 / 9;
    border: 0;
    border-radius: 6px;
}

@media (prefers-reduced-motion: no-preference) {
    .consent-bar {
        animation: consent-bar-in 200ms ease-out;
    }

    @keyframes consent-bar-in {
        from { transform: translateY(100%); }
        to { transform: translateY(0); }
    }
}

@media (max-width: 700px) {
    .consent-bar__actions {
        width: 100%;
    }

        .consent-bar__actions button {
            flex: 1 1 auto;
        }
}
```

- [ ] **Step 3: Include the stylesheet and the partial**

In `Views/Root.cshtml`, after the existing stylesheet link:

```razor
<link href="~/static/css/consent.css" rel="stylesheet" />
```

And immediately before the closing `</body>` tag:

```razor
@await Html.PartialAsync("_ConsentBanner")
```

- [ ] **Step 4: Verify visually and by keyboard**

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:13226" ./bin/Debug/net10.0/NDSTK.exe &
curl -s http://localhost:13226/ | grep -E 'consent-bar|consent-dialog|btn-secondary'
```

Expected: the bar markup, the dialog markup and both buttons present.

Then open `http://localhost:13226/` in a browser and check, without touching the mouse: Tab reaches the three bar buttons in order; each has a visible focus ring; "Anpassa" does nothing yet (Task 10 wires it). Stop the site.

- [ ] **Step 5: Commit**

```bash
git add Views/Partials/_ConsentBanner.cshtml wwwroot/static/css/consent.css Views/Root.cshtml
git commit -m "feat(consent): add consent bar and settings dialog markup and styles"
```

---

### Task 10: Banner behaviour

**Files:**
- Create: `wwwroot/static/js/consent.js`

**Interfaces:**
- Consumes: the DOM hooks from Task 9; `POST /api/consent` from Task 3.
- Produces: `window.ndstkConsent` with `open()`, `close()`, `get()`, `has(category)`, `onChange(fn)`; the `ndstk:consent-change` DOM event.

- [ ] **Step 1: Write the script**

`wwwroot/static/js/consent.js`:

```javascript
/**
 * NDSTK cookie consent.
 *
 * Deliberately dependency-free and self-hosted: a consent tool that itself calls out to a third
 * party would undercut its own purpose.
 *
 * The server is the source of truth. This script never writes the consent cookie - it posts the
 * choice and lets the endpoint set it, which is what guarantees the cookie's attributes are right.
 */
(function () {
    'use strict';

    var script = document.currentScript;
    var endpoint = script.getAttribute('data-consent-endpoint') || '/api/consent';
    var cookieName = script.getAttribute('data-consent-cookie') || 'ndstk-consent';
    var policyVersion = parseInt(script.getAttribute('data-consent-version') || '1', 10);
    var consentModeEnabled = script.getAttribute('data-consent-mode') === 'on';

    var listeners = [];

    function readCookie() {
        var prefix = cookieName + '=';
        var parts = document.cookie ? document.cookie.split('; ') : [];

        for (var i = 0; i < parts.length; i++) {
            if (parts[i].indexOf(prefix) !== 0) { continue; }
            try {
                var parsed = JSON.parse(decodeURIComponent(parts[i].substring(prefix.length)));
                if (!parsed || typeof parsed.v !== 'number') { return null; }
                return {
                    version: parsed.v,
                    decidedAt: parsed.t,
                    categories: Array.isArray(parsed.c) ? parsed.c : [],
                    consentId: parsed.id
                };
            } catch (error) {
                return null;
            }
        }

        return null;
    }

    function currentCategories() {
        var state = readCookie();
        if (!state || state.version < policyVersion) { return []; }
        return state.categories;
    }

    function has(category) {
        return category === 'necessary' || currentCategories().indexOf(category) !== -1;
    }

    var dialog = document.getElementById('consent-dialog');
    var bar = document.querySelector('[data-consent-bar]');

    function open() {
        if (!dialog) { return; }
        if (typeof dialog.showModal === 'function') {
            dialog.showModal();
        } else {
            dialog.setAttribute('open', 'open');
        }
    }

    function close() {
        if (!dialog) { return; }
        if (typeof dialog.close === 'function') {
            dialog.close();
        } else {
            dialog.removeAttribute('open');
        }
    }

    /** Turn inert `type="text/plain"` placeholders into live scripts for the granted categories. */
    function activateScripts() {
        var blocked = document.querySelectorAll('script[type="text/plain"][data-consent-category]');

        Array.prototype.forEach.call(blocked, function (placeholder) {
            if (!has(placeholder.getAttribute('data-consent-category'))) { return; }

            var live = document.createElement('script');
            var src = placeholder.getAttribute('data-src');

            if (src) {
                live.src = src;
            } else {
                live.text = placeholder.textContent;
            }

            placeholder.parentNode.replaceChild(live, placeholder);
        });
    }

    function updateConsentMode() {
        if (!consentModeEnabled || typeof window.gtag !== 'function') { return; }

        var marketing = has('marketing') ? 'granted' : 'denied';

        window.gtag('consent', 'update', {
            ad_storage: marketing,
            ad_user_data: marketing,
            ad_personalization: marketing,
            analytics_storage: has('statistics') ? 'granted' : 'denied',
            functionality_storage: has('preferences') ? 'granted' : 'denied',
            personalization_storage: has('preferences') ? 'granted' : 'denied'
        });
    }

    function announce() {
        var detail = { categories: currentCategories(), version: policyVersion };

        document.dispatchEvent(new CustomEvent('ndstk:consent-change', { detail: detail }));
        listeners.forEach(function (listener) {
            try { listener(detail); } catch (error) { /* a bad subscriber must not break consent */ }
        });
    }

    function selectedCategories() {
        var inputs = document.querySelectorAll('[data-consent-category-input]');

        return Array.prototype.filter.call(inputs, function (input) {
            return input.checked && !input.disabled;
        }).map(function (input) {
            return input.value;
        });
    }

    function send(action, categories) {
        return fetch(endpoint, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            body: JSON.stringify({
                categories: categories,
                action: action,
                culture: document.documentElement.lang || null
            })
        }).then(function (response) {
            if (!response.ok) { throw new Error('Consent request failed: ' + response.status); }
            return response.json();
        }).then(function () {
            close();
            if (bar) { bar.hidden = true; }
            activateScripts();
            updateConsentMode();
            announce();
        }).catch(function (error) {
            // Leave the bar in place: a failed request must not read as a recorded choice.
            if (window.console) { console.error(error); }
        });
    }

    function decide(action) {
        if (action === 'accept-all') { return send(action, ['preferences', 'statistics', 'marketing']); }
        if (action === 'reject-all') { return send(action, []); }
        if (action === 'withdrawn') {
            return send(action, []).then(function () { window.location.reload(); });
        }
        return send('custom', selectedCategories());
    }

    document.addEventListener('click', function (event) {
        var opener = event.target.closest('[data-consent-open]');
        if (opener) { event.preventDefault(); open(); return; }

        var closer = event.target.closest('[data-consent-close]');
        if (closer) { event.preventDefault(); close(); return; }

        var actor = event.target.closest('[data-consent-action]');
        if (actor) { event.preventDefault(); decide(actor.getAttribute('data-consent-action')); }
    });

    // Anything already granted from a previous visit becomes live on this page load too.
    activateScripts();
    updateConsentMode();

    window.ndstkConsent = {
        open: open,
        close: close,
        get: readCookie,
        has: has,
        onChange: function (fn) { if (typeof fn === 'function') { listeners.push(fn); } }
    };
})();
```

Two behaviours worth keeping: a failed request leaves the bar visible, because a silent failure that hides the bar would read as a recorded choice that does not exist; and `withdrawn` reloads, because server-emitted scripts cannot be un-emitted client-side.

- [ ] **Step 2: Verify the full flow**

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:13226" ./bin/Debug/net10.0/NDSTK.exe &
```

In a browser at `http://localhost:13226/`, using only the keyboard:

1. Tab to "Anpassa", press Enter. The dialog opens and focus moves inside it.
2. Tab through — focus stays inside the dialog and cycles.
3. Press Esc. The dialog closes.
4. Tab to "Neka alla", press Enter. The bar disappears.
5. Reload. The bar does **not** return.
6. In devtools, confirm the `ndstk-consent` cookie exists with `c` empty.
7. Run `ndstkConsent.has('statistics')` in the console — expect `false`.
8. Delete the cookie, reload, press "Godkänn alla", then run `ndstkConsent.get()` — expect all three categories.

Stop the site.

- [ ] **Step 3: Commit**

```bash
git add wwwroot/static/js/consent.js
git commit -m "feat(consent): add consent banner behaviour and public JS API"
```

---

### Task 11: Cookie policy page template

**Files:**
- Modify: `Views/CookiePolicy.cshtml`

**Interfaces:**
- Consumes: `cookiePolicy` document type from Task 7, Dictionary keys from Task 8, `ConsentCategories`.
- Produces: the rendered policy page.

- [ ] **Step 1: Write the template**

Replace `Views/CookiePolicy.cshtml` with:

```razor
@using NDSTK.Consent
@using Umbraco.Cms.Core.Models.Blocks
@using Umbraco.Cms.Core.Strings
@inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage
@inject IConsentState Consent
@{
    Layout = "Root.cshtml";

    var declared = Model.Value<BlockListModel>("cookies") ?? [];
    var byCategory = declared
        .GroupBy(block => block.Content.Value<string>("category") ?? "necessary")
        .ToDictionary(group => group.Key, group => group.ToList());

    var granted = Consent.Decision?.Granted ?? new HashSet<ConsentCategory>();
}

<div class="content-blocks">
    <article class="post">
        <h1>@(Model.Value<string>("heading").IfNullOrWhiteSpace(Model.Name))</h1>
        @(Model.Value<IHtmlEncodedString>("introduction"))
    </article>

    <article class="post">
        <h2>@(Umbraco.GetDictionaryValueOrDefault("Cookies.Policy.CurrentChoice", "Ditt nuvarande val"))</h2>
        @if (Consent.NeedsDecision)
        {
            <p>@(Umbraco.GetDictionaryValueOrDefault("Cookies.Policy.NoChoice", "Du har inte gjort något val än."))</p>
        }
        else
        {
            <ul>
                @foreach (var category in ConsentCategories.All)
                {
                    var name = Umbraco.GetDictionaryValueOrDefault($"Cookies.Category.{category}.Name",
                        ConsentCategories.ToWireName(category));
                    var isOn = category == ConsentCategory.Necessary || granted.Contains(category);
                    <li>@name: <strong>@(isOn ? "på" : "av")</strong></li>
                }
            </ul>
        }
        <p>
            <button type="button" class="btn-primary" data-consent-open>@(Umbraco.GetDictionaryValueOrDefault("Cookies.Policy.Reopen", "Ändra inställningar"))</button>
            @if (Consent.NeedsDecision is false)
            {
                <button type="button" class="btn-secondary" data-consent-action="withdrawn">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Policy.Withdraw", "Återkalla samtycke"))</button>
            }
        </p>
    </article>

    @foreach (var category in ConsentCategories.All)
    {
        var wire = ConsentCategories.ToWireName(category);
        if (byCategory.TryGetValue(wire, out var cookies) is false || cookies.Count == 0)
        {
            continue;
        }

        <article class="post">
            <h2>@(Umbraco.GetDictionaryValueOrDefault($"Cookies.Category.{category}.Name", wire))</h2>
            <p>@(Umbraco.GetDictionaryValueOrDefault($"Cookies.Category.{category}.Description", string.Empty))</p>

            <div class="cookie-table-wrapper">
                <table class="cookie-table">
                    <thead>
                        <tr>
                            <th scope="col">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Table.Name", "Namn"))</th>
                            <th scope="col">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Table.Provider", "Leverantör"))</th>
                            <th scope="col">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Table.Purpose", "Syfte"))</th>
                            <th scope="col">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Table.Duration", "Lagringstid"))</th>
                            <th scope="col">@(Umbraco.GetDictionaryValueOrDefault("Cookies.Table.Type", "Typ"))</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var cookie in cookies)
                        {
                            <tr>
                                <td><code>@(cookie.Content.Value<string>("cookieName"))</code></td>
                                <td>@(cookie.Content.Value<string>("provider"))</td>
                                <td>@(cookie.Content.Value<string>("purpose"))</td>
                                <td>@(cookie.Content.Value<string>("duration"))</td>
                                <td>@(cookie.Content.Value<string>("storageType"))</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        </article>
    }

    @if (Model.Value<IHtmlEncodedString>("outro") is not null)
    {
        <article class="post">
            @(Model.Value<IHtmlEncodedString>("outro"))
        </article>
    }
</div>
```

Categories with nothing declared are skipped rather than rendered empty, so the page stays truthful as the site grows.

- [ ] **Step 2: Add the table styles**

Append to `wwwroot/static/css/consent.css`:

```css
/* Cookie policy tables. Wide content scrolls inside its own container so the page body never does. */
.cookie-table-wrapper {
    overflow-x: auto;
}

.cookie-table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.9rem;
}

    .cookie-table th,
    .cookie-table td {
        text-align: left;
        vertical-align: top;
        padding: 0.5rem 0.65rem;
        border-bottom: 1px solid #d5d7db;
    }

    .cookie-table th {
        color: var(--primary);
        border-bottom: 2px solid var(--accent);
        white-space: nowrap;
    }

    .cookie-table code {
        word-break: break-all;
    }
```

- [ ] **Step 3: Commit**

```bash
git add Views/CookiePolicy.cshtml wwwroot/static/css/consent.css
git commit -m "feat(consent): render the cookie policy page from the registry"
```

---

### Task 12: Seed the policy page and wire the footer

**Files:**
- Modify: `ContentModel/NdstkContentSeeder.cs`
- Modify: `Views/Root.cshtml`

**Interfaces:**
- Consumes: `cookiePolicy` document type, `NdstkKeys.Nodes.CookiePolicy`.
- Produces: a published Cookie policy node with a pre-filled registry; `Settings.cookiePolicyPage` wired to it; a footer link that reopens the banner.

- [ ] **Step 1: Seed the node**

In `ContentModel/NdstkContentSeeder.cs`, add this method:

```csharp
private IContent SeedCookiePolicy(IContent start)
{
    IContent policy = Create("Cookies", start.Id, "cookiePolicy", Nodes.CookiePolicy);
    policy.SetValue("heading", "Kakor på ndstk.se");
    policy.SetValue("introduction",
        "<p>Vi använder kakor (cookies) för att sajten ska fungera. Nedan ser du exakt vilka kakor vi " +
        "sätter, varför, och hur länge de sparas.</p>");
    policy.SetValue("outro",
        "<p>Du kan även blockera och radera kakor i din webbläsares inställningar. Har du frågor, " +
        "kontakta oss på <a href=\"mailto:info@ndstk.se\">info@ndstk.se</a>. Du kan läsa mer om kakor " +
        "hos Integritetsskyddsmyndigheten.</p>");

    // Only what this site genuinely sets today. An invented table would be worse than a short one.
    policy.SetValue("cookies", BlockList(
        Block(ElementTypes.CookieDefinition,
            ("cookieName", "ndstk-consent"),
            ("provider", "NDSTK"),
            ("category", Dropdown("necessary")),
            ("purpose", "Sparar ditt val av kakor så att vi inte behöver fråga igen."),
            ("duration", "12 månader"),
            ("storageType", Dropdown("Cookie"))),
        Block(ElementTypes.CookieDefinition,
            ("cookieName", ".AspNetCore.Antiforgery.*"),
            ("provider", "NDSTK"),
            ("category", Dropdown("necessary")),
            ("purpose", "Skyddar formulär mot förfalskade anrop."),
            ("duration", "Session"),
            ("storageType", Dropdown("Cookie"))),
        Block(ElementTypes.CookieDefinition,
            ("cookieName", "UMB_MEMBER"),
            ("provider", "NDSTK"),
            ("category", Dropdown("necessary")),
            ("purpose", "Håller dig inloggad som medlem efter inloggning med BankID."),
            ("duration", "Session"),
            ("storageType", Dropdown("Cookie")))));

    contentService.Save(policy, UserId);
    return policy;
}
```

- [ ] **Step 2: Call it and link it from Settings**

In `Seed()`, after the `error` node is saved and **before** `SeedSettings(...)`:

```csharp
IContent cookiePolicy = SeedCookiePolicy(start);
```

Change the `SeedSettings` signature to `SeedSettings(IContent start, IContent articles, IContent login, IContent cookiePolicy)`, pass `cookiePolicy` at the call site, and add inside it:

```csharp
settings.SetValue("cookiePolicyPage", Node(cookiePolicy));
```

Add `cookiePolicy` to the publish list, positioned after `settings`:

```csharp
foreach (IContent node in new[] { start, settings, articles, login, error, cookiePolicy }.Concat(posts))
```

Ordering is load-bearing — publishing a child before its parent fails with `FailedPublishPathNotPublished`.

- [ ] **Step 3: Add the footer link**

In `Views/Root.cshtml`, replace the `<footer>` contents with:

```razor
    <footer>
        <p>@footerText</p>
        <p>
            <button type="button" class="btn-link" data-consent-open>@(Umbraco.GetDictionaryValueOrDefault("Cookies.Footer.Link", "Cookieinställningar"))</button>
            @if (settings?.Value<IPublishedContent>("cookiePolicyPage") is IPublishedContent cookiePage)
            {
                <a href="@cookiePage.Url()">@cookiePage.Name</a>
            }
        </p>
    </footer>
```

And add to `wwwroot/static/css/consent.css`:

```css
footer .btn-link {
    color: white;
}

footer p {
    margin: 0.25rem 0;
}
```

- [ ] **Step 4: Verify against a fresh database**

The seeder only runs when the content tree is empty, so verifying this needs an empty tree. Either point `ConnectionStrings:umbracoDbDSN` at a fresh SQLite file, or delete the root content in the backoffice and empty the recycle bin, then restart.

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:13226" ./bin/Debug/net10.0/NDSTK.exe &
```

Expected in the log: `NDSTK start page seeded.` with `Content Cookies (id=…) has been published.` and no `FailedPublishPathNotPublished` warnings.

```bash
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:13226/cookies/
curl -s http://localhost:13226/cookies/ | grep -cE 'cookie-table|ndstk-consent|UMB_MEMBER'
curl -s http://localhost:13226/ | grep -c 'data-consent-open'
```

Expected: `200`; a non-zero count for the table markup; at least 1 for the footer button. Stop the site.

- [ ] **Step 5: Commit**

```bash
git add ContentModel/NdstkContentSeeder.cs Views/Root.cshtml wwwroot/static/css/consent.css
git commit -m "feat(consent): seed the cookie policy page and add the footer settings link"
```

---

### Task 13: Emit Consent Mode defaults

**Files:**
- Modify: `Views/Root.cshtml`

**Interfaces:**
- Consumes: `ConsentModeScript`, `ConsentOptions`, `IConsentState`.
- Produces: the Consent Mode v2 `default` and `update` calls in `<head>`.

- [ ] **Step 1: Emit the snippets**

In `Views/Root.cshtml`, add to the `@using`/`@inject` block:

```razor
@using Microsoft.Extensions.Options
@using NDSTK.Consent
@inject IConsentState Consent
@inject IOptions<ConsentOptions> ConsentSettings
```

Then, as the **first** element inside `<head>` — before the `<meta>` tags, because the default call has to run before any Google tag:

```razor
    @if (string.IsNullOrWhiteSpace(ConsentSettings.Value.GoogleMeasurementId) is false)
    {
        <script>@Html.Raw(ConsentModeScript.Defaults())@Html.Raw(ConsentModeScript.Update(Consent))</script>
        <consent-script category="statistics" async
                        src="https://www.googletagmanager.com/gtag/js?id=@ConsentSettings.Value.GoogleMeasurementId"></consent-script>
    }
```

Nothing at all is emitted while `GoogleMeasurementId` is unset, which is the site's current state — no dead script on every page. The `<consent-script>` is what proves the gating works end to end: with `statistics` denied, the Google tag is absent from the HTML entirely.

- [ ] **Step 2: Verify both states**

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:13226" ./bin/Debug/net10.0/NDSTK.exe &
curl -s http://localhost:13226/ | grep -c googletagmanager
```

Expected: `0` — no measurement id configured.

Now stop the site, add to `appsettings.Development.json`:

```json
"Ndstk": { "Consent": { "GoogleMeasurementId": "G-TEST123" } }
```

Restart and re-run the two checks:

```bash
curl -s http://localhost:13226/ | grep -c "gtag('consent','default'"
curl -s http://localhost:13226/ | grep -c googletagmanager
```

Expected: `1` for the default call, and `0` for the tag itself — because no consent cookie is present, so `<consent-script>` suppresses it. That single pair of results is the whole design working: the consent framework is live, and the tracker is not.

Then accept statistics in the browser, reload, and confirm `googletagmanager` now appears. Remove the test measurement id, and stop the site.

- [ ] **Step 3: Commit**

```bash
git add Views/Root.cshtml
git commit -m "feat(consent): emit Google Consent Mode v2 defaults ahead of any tag"
```

---

## Self-review notes

**Spec coverage.** §1 → Task 1. §2 → Tasks 1–2. §3 → Task 3 (endpoint and cookie only; the log table is stage 7, out of scope). §4 → Tasks 4, 5, 10. §5 → Tasks 6, 13. §6 → Task 7. §7 → Tasks 7, 11. §8 → out of scope (stage 8). §9 → Task 8. §10 → Tasks 9, 10. §11 → Tasks 8, 12. §12 → test steps throughout; the Umbraco-booting integration rig is deferred to stage 7 as §12 states.

**Known gap carried forward.** The endpoint currently validates `action` and then discards it. Stage 7 must write it to the log. Task 3 Step 5 says so in a comment so it cannot be silently forgotten.

**Manual-only coverage.** Keyboard behaviour (Tasks 9, 10) and the browser-side Consent Mode transition (Task 13) are verified by hand, not by automated tests. A real screen-reader pass is out of scope and is not claimed.
