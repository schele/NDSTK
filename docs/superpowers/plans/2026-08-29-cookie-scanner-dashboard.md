# Cookie Scanner Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the cookie scanner's WinForms window with a desktop dashboard — a single exe that opens its own window and renders an embedded HTML/CSS/JS front end over the existing scan runner.

**Architecture:** A `net10.0-windows` WinExe whose entire client area is one WebView2 control. The dashboard's files are embedded resources served over `https://app.localhost/` through `WebResourceRequested`; the page and the host talk in one JSON envelope both ways (`PostWebMessageAsJson` out, `chrome.webview.postMessage` in). The engine is untouched apart from two additive record fields. `NDSTK.CookieScanner.Gui` is deleted in the final task, after the dashboard has been verified doing everything it did.

**Tech Stack:** .NET 10, WebView2 (`Microsoft.Web.WebView2` 1.0.4129.50), WinForms as a bare shell, Lit 3.3.x vendored with no build step, inline SVG, Inter Variable, Playwright 1.62.0 (unchanged), xUnit.

**Spec:** [docs/superpowers/specs/2026-08-28-cookie-scanner-dashboard-design.md](../specs/2026-08-28-cookie-scanner-dashboard-design.md)

## Global Constraints

- **.NET 10**, nullable reference types enabled, implicit usings enabled.
- **`NDSTK.CookieScanner` must NOT reference WinForms or WebView2** and must stay `net10.0`, so the CLI can still be published for a non-Windows build agent. Only the new Desktop project targets `net10.0-windows`. The dependency runs one way: the window references the scanner, never the reverse.
- **`NDSTK.CookieScan.Core` keeps zero dependencies** — no `PackageReference`, no `ProjectReference`.
- **The CLI's behaviour does not change.** Same flags, same output, same exit codes: violations → 1; a write-back that was configured, attempted and failed → 2; otherwise 0. Task 9's gate compares stdout, stderr and `cookie-scan-report.md` byte-for-byte against the pre-change build. `cookie-scan-report.json` legitimately gains one key in Task 1 and is not part of that comparison.
- **The client secret and the member password are never persisted.** The secret comes only from `NDSTK_COOKIESCAN_CLIENT_SECRET`; the member password is typed per run. No settings field may exist that could hold either.
- **Anything resolved "beside the exe" uses `Path.GetDirectoryName(Environment.ProcessPath)`, never `AppContext.BaseDirectory`** — the published exes set `IncludeAllContentForSelfExtract`, under which `BaseDirectory` is the extraction directory under `%TEMP%\.net`.
- **Visitor-facing copy is Swedish**; identifiers, comments, log messages and UI labels are English.
- **The dashboard has no build step.** No `package.json`, no npm, no Vite. `dotnet publish` stays the only build.
- **`NDSTK.csproj` sits at the repository root**, so every sibling project directory must be listed in its `DefaultItemExcludes` or the SDK's default globs pull those sources into the web assembly and the build fails with duplicate assembly attributes.
- **The user starts and stops the site.** No task starts or restarts it. Where a task needs the site running, it says so. **Never build `NDSTK.csproj` or `NDSTK.slnx`** unless the task says the site is down.
- **The user commits manually.** No task contains a `git commit` step; each ends with a verification checkpoint.
- **Site under test:** `https://localhost:44351`. The consent endpoint throttles at 10 posts per IP per minute; a full scan posts 6, a member scan 7 — leave a minute between runs. A full scan takes about 50 seconds.
- **Branch:** create `feature/cookie-scanner-dashboard` from `master` before Task 1.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `NDSTK.CookieScanner/ScanResult.cs` | gains `ScanOptionsSummary? Options` |
| `NDSTK.CookieScanner/ScanHistory.cs` | `ScanHistoryEntry` gains `ViolationCount` |
| `NDSTK.CookieScanner/ScanRunner.cs` | populates `Options` |
| `NDSTK.CookieScanner.Desktop/NDSTK.CookieScanner.Desktop.csproj` | the new front end's project |
| `NDSTK.CookieScanner.Desktop/Program.cs` | entry point |
| `NDSTK.CookieScanner.Desktop/DashboardForm.cs` | the window: WebView2 lifecycle, hardening, navigation |
| `NDSTK.CookieScanner.Desktop/DashboardAssets.cs` | URL path → embedded resource stream + MIME |
| `NDSTK.CookieScanner.Desktop/DashboardMessages.cs` | the envelope types, both directions |
| `NDSTK.CookieScanner.Desktop/DashboardBridge.cs` | routes commands, posts replies, buffers until `ready` |
| `NDSTK.CookieScanner.Desktop/ScanSession.cs` | owns one run: options, cancellation, report + history writes |
| `NDSTK.CookieScanner.Desktop/WebViewScanLog.cs` | `IScanLog` that marshals and posts log envelopes |
| `NDSTK.CookieScanner.Desktop/DashboardSettings.cs` | the six remembered fields, ported |
| `NDSTK.CookieScanner.Desktop/wwwroot/index.html` | the shell: sidebar, page host |
| `NDSTK.CookieScanner.Desktop/wwwroot/app.css` | tokens, reset, layout, `@font-face` |
| `NDSTK.CookieScanner.Desktop/wwwroot/app.js` | bridge client, hash router, page wiring |
| `NDSTK.CookieScanner.Desktop/wwwroot/components/*.js` | the six Lit elements |
| `NDSTK.CookieScanner.Desktop/wwwroot/vendor/lit.js` | Lit 3.3.x, vendored |
| `NDSTK.CookieScanner.Desktop/wwwroot/fonts/` | `inter-latin-wght-normal.woff2`, `OFL.txt` |
| `NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj` | **new**: the Windows-only test project |
| `NDSTK.Desktop.Tests/DashboardAssetsTests.cs` | every referenced asset resolves; unknown → 404 |
| `NDSTK.Desktop.Tests/DashboardMessageTests.cs` | each command parses; unknown type ignored |
| `NDSTK.Tests/ScanJsonTests.cs`, `ScanHistoryTests.cs` | extended for Task 1 |

**Why a second test project.** `NDSTK.Tests` targets `net10.0` and cannot reference a `net10.0-windows` project. Multi-targeting it would double every existing test on Windows and make the suite's count meaningless. A separate `net10.0-windows` project keeps the portable suite portable and its count stable.

---

## Task 1: The two additive record fields

**Files:**
- Modify: `NDSTK.CookieScanner/ScanResult.cs`, `NDSTK.CookieScanner/ScanHistory.cs`, `NDSTK.CookieScanner/ScanRunner.cs`
- Test: `NDSTK.Tests/ScanJsonTests.cs`, `NDSTK.Tests/ScanHistoryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `sealed record ScanOptionsSummary(int MaxPages, Locale Locale, bool MemberScanEnabled, bool DryRun)`; `ScanResult` gains a final positional member `ScanOptionsSummary? Options`; `ScanHistoryEntry` becomes `(string Path, DateTimeOffset CompletedAt, string Site, int EntryCount, int ViolationCount, int ExitCode)`. Tasks 6, 7 and 8 consume both.

The last plan's final review found that two scans run with different options diff as though the *site* had changed, and that nothing in the file said otherwise. `Options` is nullable so a history file written before this change loads as "not recorded" rather than as a false all-clear. `ViolationCount` costs nothing: `ScanHistory.List()` already deserialises every file and throws that number away.

- [ ] **Step 1: Write the failing tests**

Add to `NDSTK.Tests/ScanJsonTests.cs`:

```csharp
    // The options that shape a scan are part of its record, because two scans run with different
    // options diff as though the site changed - a member scan against a public one differs by the
    // member cookie, which is an artefact of the run and not a change to the site.
    [Fact]
    public void The_options_summary_round_trips()
    {
        ScanResult sample = Sample() with
        {
            Options = new ScanOptionsSummary(MaxPages: 7, Locale: Locale.En, MemberScanEnabled: true, DryRun: false),
        };

        ScanResult? back = ScanJson.Deserialize(ScanJson.Serialize(sample));

        Assert.NotNull(back?.Options);
        Assert.Equal(7, back.Options.MaxPages);
        Assert.Equal(Locale.En, back.Options.Locale);
        Assert.True(back.Options.MemberScanEnabled);
        Assert.False(back.Options.DryRun);
    }

    // A history file written before this field existed must still load, and must say "not recorded"
    // rather than claiming a default that was never true.
    [Fact]
    public void A_result_without_an_options_summary_still_loads()
    {
        string json = ScanJson.Serialize(Sample() with { Options = null });

        Assert.DoesNotContain("\"options\": {", json);

        ScanResult? back = ScanJson.Deserialize(json);

        Assert.NotNull(back);
        Assert.Null(back.Options);
        Assert.Single(back.Candidates);
    }
```

`Sample()` already exists in that file; give it `Options: null` in its constructor call so the two tests above control the field explicitly.

Add to `NDSTK.Tests/ScanHistoryTests.cs`:

```csharp
    // The trend chart needs a violation count per scan. List() already parses every file, so the
    // count is free here and would otherwise cost a second read of all fifty.
    [Fact]
    public void An_entry_carries_the_violation_count()
    {
        var history = new ScanHistory(folder);
        history.SaveResult(Result(new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero), candidates: 3) with
        {
            Violations =
            [
                new CookieDeclarationCandidate("_fbp", "Meta", "marketing", "Annonser.", "3 månader",
                    "Cookie", CandidateFlag.Violation, ConsentPass.RejectAll, "https://ndstk.se/"),
            ],
        });

        ScanHistoryEntry entry = Assert.Single(history.List());

        Assert.Equal(3, entry.EntryCount);
        Assert.Equal(1, entry.ViolationCount);
        Assert.Equal(1, entry.ExitCode);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter "ScanJsonTests|ScanHistoryTests"`
Expected: build failure — `CS0117: 'ScanResult' does not contain a definition for 'Options'` and `CS0117` for `ViolationCount`.

- [ ] **Step 3: Add `ScanOptionsSummary` and the `Options` member**

In `NDSTK.CookieScanner/ScanResult.cs`, above `ScanResult`:

```csharp
/// <summary>The options that shaped a scan, as much of them as a comparison needs.</summary>
/// <remarks>
/// Recorded because two scans run with different options diff as though the site had changed: a
/// member scan finds the member cookie and a public scan does not, which is a property of the run
/// rather than of the site. Nullable, because a history file written before this existed must load
/// and say "not recorded" instead of claiming a default nobody chose.
/// <para>
/// Deliberately not the whole <see cref="ScanOptions"/>: that record carries the client secret and
/// the member password, and neither may ever reach a file.
/// </para>
/// </remarks>
public sealed record ScanOptionsSummary(int MaxPages, Locale Locale, bool MemberScanEnabled, bool DryRun);
```

and add `ScanOptionsSummary? Options` as the **last** positional member of `ScanResult`, after `string Site`.

- [ ] **Step 4: Populate it in `ScanRunner`**

In `RunAsync`'s `return new ScanResult(...)`, add the final argument:

```csharp
            new ScanOptionsSummary(options.MaxPages, options.Locale, options.MemberScanEnabled, options.DryRun));
```

- [ ] **Step 5: Add `ViolationCount` to `ScanHistoryEntry`**

Change the record to:

```csharp
public sealed record ScanHistoryEntry(
    string Path,
    DateTimeOffset CompletedAt,
    string Site,
    int EntryCount,
    int ViolationCount,
    int ExitCode);
```

and in `ScanHistory.List()`, pass `result.Violations.Count` in the new position:

```csharp
            entries.Add(new ScanHistoryEntry(
                path, result.CompletedAt, result.Site, result.Candidates.Count,
                result.Violations.Count, result.ExitCode));
```

- [ ] **Step 6: Fix the one other construction site**

Run: `grep -rn "new ScanHistoryEntry(" --include=*.cs . | grep -v "/obj/"`
Every hit outside `ScanHistory.cs` is a test fixture; add the count in the right position. `NDSTK.CookieScanner.Gui` also consumes `ScanHistoryEntry` but only reads named properties, so it needs no change — confirm that with `grep -n "ScanHistoryEntry" NDSTK.CookieScanner.Gui/*.cs` and report what you find.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj --filter "ScanJsonTests|ScanHistoryTests"`
Expected: all pass, including the three new ones.

- [ ] **Step 8: Verification checkpoint**

Run: `dotnet build NDSTK.CookieScanner/NDSTK.CookieScanner.csproj` — succeeded, 0 warnings.
Run: `dotnet build NDSTK.CookieScanner.Gui/NDSTK.CookieScanner.Gui.csproj` — succeeded; the old window must still compile until Task 9 deletes it.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — green, no cookie-scanner test failing.

**Then confirm the CLI's own output is untouched**, with the site up:

```bash
dotnet run --project NDSTK.CookieScanner -- --url https://localhost:44351 --max-pages 7 --report-dir ./scan-out
```

Expected: stdout identical in wording and order to before this task; `cookie-scan-report.md` unchanged; `cookie-scan-report.json` now carries a top-level `"options"` object with `maxPages`, `locale` as a name, `memberScanEnabled` and `dryRun`. Paste the JSON's `options` object into your report. Run `rm -rf ./scan-out`.

Run: `git status --short` — the five files of this task.

---

## Task 2: The window, and the asset pipeline

**Files:**
- Create: `NDSTK.CookieScanner.Desktop/NDSTK.CookieScanner.Desktop.csproj`, `Program.cs`, `DashboardForm.cs`, `DashboardAssets.cs`, `wwwroot/index.html`
- Create: `NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj`, `NDSTK.Desktop.Tests/DashboardAssetsTests.cs`
- Modify: `NDSTK.slnx`, `NDSTK.csproj` (`DefaultItemExcludes`)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `DashboardForm`, and `static class DashboardAssets` with `bool TryOpen(string path, out Stream content, out string contentType)`. Tasks 3–9 all render through this pipeline.

The deliverable is a window that opens and renders a page served from inside the exe. Everything after this task is content.

- [ ] **Step 1: Create the project**

`NDSTK.CookieScanner.Desktop/NDSTK.CookieScanner.Desktop.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>NDSTK.CookieScanner.Desktop</RootNamespace>
    <!--
      The retired WinForms window's assembly name, deliberately: the published exe, the publish
      command and every line of docs/cookie-scanner.md stay as they are, and the two never ship
      together.
    -->
    <AssemblyName>ndstk-cookiescan-ui</AssemblyName>
    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
    <!--
      The same three the console tool needs, for the same reason: Playwright's driver ships node.exe,
      which the SDK classifies as content rather than a native library. WebView2Loader.dll rides
      along on the native switch - the bundler classifies by content, not by item type.
    -->
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.4129.50" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\NDSTK.CookieScan.Core\NDSTK.CookieScan.Core.csproj" />
    <ProjectReference Include="..\NDSTK.CookieScanner\NDSTK.CookieScanner.csproj" />
  </ItemGroup>

  <!--
    The dashboard lives inside the exe. LogicalName keeps the relative path, so the resolver can map
    a URL straight onto a resource name without a lookup table that would drift.
  -->
  <ItemGroup>
    <EmbeddedResource Include="wwwroot\**\*">
      <LogicalName>wwwroot/$([System.String]::Copy('%(RecursiveDir)%(Filename)%(Extension)').Replace('\','/'))</LogicalName>
    </EmbeddedResource>
  </ItemGroup>

</Project>
```

Add to `NDSTK.slnx` after the `NDSTK.CookieScanner` entry:

```xml
  <Project Path="NDSTK.CookieScanner.Desktop/NDSTK.CookieScanner.Desktop.csproj" />
  <Project Path="NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj" />
```

and append `;NDSTK.CookieScanner.Desktop\**;NDSTK.Desktop.Tests\**` to `DefaultItemExcludes` in `NDSTK.csproj` (line 14), changing nothing else in that file.

- [ ] **Step 2: Write the placeholder page**

`NDSTK.CookieScanner.Desktop/wwwroot/index.html` — replaced wholesale in Task 3, and deliberately proves the pipeline rather than looking like anything:

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>NDSTK cookie scanner</title>
</head>
<body>
  <h1 id="hello">Serving from inside the exe.</h1>
  <p id="origin"></p>
  <script type="module">
    // A module script and localStorage both prove the origin is real: neither works from
    // NavigateToString, which is why the dashboard is served rather than injected.
    localStorage.setItem('probe', 'ok');
    document.getElementById('origin').textContent =
      `${location.origin} · localStorage ${localStorage.getItem('probe')}`;
  </script>
</body>
</html>
```

- [ ] **Step 3: Write the failing asset test**

`NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\NDSTK.CookieScanner.Desktop\NDSTK.CookieScanner.Desktop.csproj" />
  </ItemGroup>

</Project>
```

`NDSTK.Desktop.Tests/DashboardAssetsTests.cs`:

```csharp
using System.Text.RegularExpressions;
using NDSTK.CookieScanner.Desktop;

namespace NDSTK.Desktop.Tests;

public class DashboardAssetsTests
{
    [Fact]
    public void The_index_page_resolves()
    {
        Assert.True(DashboardAssets.TryOpen("/index.html", out Stream content, out string contentType));

        using (content)
        {
            Assert.Equal("text/html; charset=utf-8", contentType);
            Assert.NotEqual(0, content.Length);
        }
    }

    // The root path is what the window navigates to if a URL ever loses its filename; serving the
    // index there costs one line and turns a blank window into a working one.
    [Fact]
    public void The_root_path_resolves_to_the_index()
    {
        Assert.True(DashboardAssets.TryOpen("/", out Stream content, out _));

        content.Dispose();
    }

    [Fact]
    public void An_unknown_path_does_not_resolve()
    {
        Assert.False(DashboardAssets.TryOpen("/nope.js", out _, out _));
    }

    // A path that climbs out of wwwroot must not reach another embedded resource.
    [Fact]
    public void A_traversing_path_does_not_resolve()
    {
        Assert.False(DashboardAssets.TryOpen("/../NDSTK.CookieScanner.Desktop.dll", out _, out _));
    }

    // The test that earns its keep: it fails when a file is renamed in one place and not the other -
    // a font, a component, the vendored Lit bundle - which is otherwise a blank page at runtime with
    // a 404 nobody sees.
    [Fact]
    public void Every_asset_the_index_references_resolves()
    {
        Assert.True(DashboardAssets.TryOpen("/index.html", out Stream content, out _));

        string html;

        using (var reader = new StreamReader(content))
        {
            html = reader.ReadToEnd();
        }

        MatchCollection references = Regex.Matches(html, @"(?:src|href)\s*=\s*""(?<path>[^""#:]+)""");

        Assert.NotEmpty(references);

        foreach (Match reference in references)
        {
            string path = reference.Groups["path"].Value;

            Assert.True(
                DashboardAssets.TryOpen(path.StartsWith('/') ? path : "/" + path, out Stream asset, out _),
                $"index.html references {path}, which is not embedded.");

            asset.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run it to verify it fails**

Run: `dotnet test NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj`
Expected: build failure, `CS0246: The type or namespace name 'DashboardAssets' could not be found`.

- [ ] **Step 5: Write `DashboardAssets`**

`NDSTK.CookieScanner.Desktop/DashboardAssets.cs`:

```csharp
using System.Reflection;

namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// The dashboard's files, which live inside the exe.
/// </summary>
/// <remarks>
/// Embedded rather than copied beside the exe: the published build is a single file, and a folder of
/// loose assets beside it would have to survive an extraction directory Microsoft documents as not
/// recommended. Nothing here touches the disk.
/// </remarks>
public static class DashboardAssets
{
    private const string Root = "wwwroot";

    private static readonly Assembly Assembly = typeof(DashboardAssets).Assembly;

    /// <summary>Opens the asset a request path names, or reports that there is none.</summary>
    public static bool TryOpen(string path, out Stream content, out string contentType)
    {
        content = Stream.Null;
        contentType = "application/octet-stream";

        string relative = path.TrimStart('/');

        if (relative.Length == 0)
        {
            relative = "index.html";
        }

        // A resource name is not a file path, so ".." cannot escape a directory here - but it can
        // still name a resource outside wwwroot, which is the same mistake with a different shape.
        if (relative.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        Stream? stream = Assembly.GetManifestResourceStream($"{Root}/{relative}");

        if (stream is null)
        {
            return false;
        }

        content = stream;
        contentType = ContentType(relative);

        return true;
    }

    private static string ContentType(string relative) => Path.GetExtension(relative).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".woff2" => "font/woff2",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj`
Expected: 5 passing.

- [ ] **Step 7: Write `Program.cs` and `DashboardForm`**

`NDSTK.CookieScanner.Desktop/Program.cs`:

```csharp
namespace NDSTK.CookieScanner.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DashboardForm());
    }
}
```

`NDSTK.CookieScanner.Desktop/DashboardForm.cs` — the shell. Requirements, in this order, because each depends on the one before:

1. Constructor: `Text = "NDSTK cookie scanner"`, `ClientSize = LogicalToDeviceUnits(new Size(1280, 860))`, `MinimumSize = LogicalToDeviceUnits(new Size(1040, 700))`, `StartPosition = FormStartPosition.CenterScreen`. **Every size goes through `LogicalToDeviceUnits`** — raw pixels render at two-thirds size on a 150% display, which is an Important finding from the last plan, not a preference.
2. A `WebView2` with `Dock = DockStyle.Fill` added to the form. Do not touch `Source` or any `CoreWebView2` member in the constructor: the control has no `CoreWebView2` until it is initialised, and constructor-time property validation is what crashed the previous window twice.
3. `OnLoad` → an `async` initialisation method:
   - `CoreWebView2Environment.GetAvailableBrowserVersionString()` inside `try`/`catch (WebView2RuntimeNotFoundException)`. On failure, `MessageBox.Show` naming the Evergreen runtime and `https://go.microsoft.com/fwlink/p/?LinkId=2124703`, then `Close()` and return.
   - `string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NDSTK.CookieScanner", "webview2");`
   - `CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions());`
   - `await webView.EnsureCoreWebView2Async(environment);`
   - Harden: `AreDefaultContextMenusEnabled`, `IsZoomControlEnabled`, `IsPinchZoomEnabled`, `IsSwipeNavigationEnabled`, `IsStatusBarEnabled`, `AreBrowserAcceleratorKeysEnabled`, `IsPasswordAutosaveEnabled`, `IsGeneralAutofillEnabled` → `false`; `AreDevToolsEnabled` → `true` only under `#if DEBUG`.
   - `core.AddWebResourceRequestedFilter("https://app.localhost/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);` — the three-argument overload; the two-argument one is deprecated and misbehaves for iframes.
   - `core.WebResourceRequested += OnWebResourceRequested;`
   - `core.Navigate("https://app.localhost/index.html");`

   Comment the user-data folder line with why: **the default is created beside the exe and fails outright in `Program Files` or on a read-only share**, which is exactly where a portable exe ends up.

   Comment the host name with why: **a made-up name under `.local` costs a ~2 second DNS resolution timeout on every navigation; names under `.localhost` resolve in tens of milliseconds.**

4. The handler:

```csharp
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        string path = new Uri(e.Request.Uri).AbsolutePath;

        if (DashboardAssets.TryOpen(path, out Stream content, out string contentType) is false)
        {
            e.Response = webView.CoreWebView2!.Environment.CreateWebResourceResponse(
                null, 404, "Not Found", "Content-Type: text/plain");

            return;
        }

        // No caching: the assets change only when the exe does, and a stale cache across an upgrade
        // would be a bug nobody could reproduce.
        e.Response = webView.CoreWebView2!.Environment.CreateWebResourceResponse(
            content, 200, "OK", $"Content-Type: {contentType}\r\nCache-Control: no-store");
    }
```

Wrap the whole initialisation in `try`/`catch (Exception error)` that shows the message and closes: a window that throws during initialisation otherwise leaves a process alive with nothing on screen, which is how the previous window's first crash hid itself.

- [ ] **Step 8: Verification checkpoint**

Run: `dotnet build NDSTK.CookieScanner.Desktop/NDSTK.CookieScanner.Desktop.csproj` — succeeded, 0 warnings.
Run: `dotnet test NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj` — 5 passing.
Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — green, unaffected.

**Then launch it and prove the pipeline**, synchronously, from PowerShell:

```powershell
$p = Start-Process -PassThru "NDSTK.CookieScanner.Desktop\bin\Debug\net10.0-windows\ndstk-cookiescan-ui.exe"
Start-Sleep -Seconds 8
"HasExited=$($p.HasExited)"
```

Then capture the window with `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` — **the window's own rectangle, never the whole screen**, and never UI Automation's `BoundingRectangle`, which includes an invisible resize border that lets neighbouring windows bleed in. Save to `.superpowers/sdd/2026-08-29-cookie-scanner-dashboard/task2-window.png`, confirm the process is alive immediately after the capture, then kill it.

The screenshot must show `https://app.localhost · localStorage ok`. That single line proves the origin is real, the module script ran, and storage works — the three things `NavigateToString` cannot give.

Run: `git status --short` — the eight files of this task.

---

## Task 3: The design system and the shell

**Files:**
- Create: `wwwroot/app.css`, `wwwroot/vendor/lit.js`, `wwwroot/fonts/inter-latin-wght-normal.woff2`, `wwwroot/fonts/OFL.txt`
- Modify: `wwwroot/index.html`

**Interfaces:**
- Consumes: `DashboardAssets` (Task 2).
- Produces: the CSS custom properties below, the sidebar/page shell, and `import { LitElement, html, css } from '/vendor/lit.js'` as the components' import path. Tasks 4–8 all build on these.

- [ ] **Step 1: Vendor Lit**

Download Lit 3.3.1 as a single ESM bundle to `wwwroot/vendor/lit.js` — `https://esm.sh/lit@3.3.1/es2022/lit.bundle.mjs`. Keep its BSD-3-Clause notice at the top of the file. This mirrors `App_Plugins/NDSTK.MemberAdmin`, which uses the same Lit major with the same no-decorator `static properties` style, so nothing new enters the repo and the CI agent still needs no npm.

Report the file's size and the first line of its licence header.

- [ ] **Step 2: Vendor the font**

Download Inter Variable's latin subset to `wwwroot/fonts/inter-latin-wght-normal.woff2` (the `@fontsource-variable/inter` distribution's file, ~48 KB) and its `OFL.txt` beside it. Self-hosted because the window must work with no internet; Google Fonts is not an option.

- [ ] **Step 3: Write `app.css`**

Tokens first, verbatim — these are the agreed palette and must not be improvised:

```css
:root {
  color-scheme: light;

  --canvas: #F4F7FC;      --surface: #FFFFFF;      --surface-2: #F9FBFE;
  --border: #E2E9F4;      --border-strong: #CFDBEC;
  --ink-900: #16202E;     --ink-600: #516079;      --ink-500: #6F7E96;  --ink-400: #95A2B8;
  --blue-600: #1D4ED8;    --blue-800: #16389E;     --blue-100: #E8EDFD; --blue-50: #F2F5FE;
  --teal-600: #0F8B7A;    --teal-800: #0A6357;     --teal-50: #E3F5F1;
  --amber-700: #9A6300;   --amber-50: #FFF6E3;
  --red-600: #C2334B;     --red-50: #FDECEF;
  --log-bg: #0F1826;      --log-ink: #C6D2E4;      --log-warn: #FFC978;

  --r-sm: 6px; --r-md: 10px; --r-lg: 12px; --r-xl: 14px; --r-pill: 999px;
  --s-1: 4px; --s-2: 8px; --s-3: 12px; --s-4: 16px; --s-5: 20px; --s-6: 24px; --s-8: 32px; --s-10: 40px;
  --shadow-sm: 0 1px 2px rgba(22,32,46,.04), 0 2px 6px rgba(29,78,216,.06);
  --shadow-md: 0 4px 12px rgba(22,32,46,.06), 0 1px 3px rgba(22,32,46,.04);

  --font-sans: 'Inter', 'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif;
  --font-mono: 'Cascadia Mono', Consolas, 'Courier New', monospace;
}

@font-face {
  font-family: 'Inter';
  src: url('/fonts/inter-latin-wght-normal.woff2') format('woff2');
  font-weight: 100 900;
  font-display: block;
}
```

Then: a minimal reset; `body` as a `grid-template-columns: 212px 1fr`; the sidebar (`--surface-2`, right border, brand block, nav links, an active pill using `--blue-100`/`--blue-800`); `.card` (surface, `--border`, `--r-xl`, `--shadow-sm`, `--s-4` padding); `.eyebrow` (10px, uppercase, `.055em` tracking, `--ink-600`); a `.tile` set with the four tints (`--blue-50`/`--red-50`/`--amber-50`/`--teal-50` with their matching 800/600 inks); table styling with the violation and needs-review row tints; `:focus-visible { outline: 2px solid var(--blue-600); outline-offset: 2px }`; `@media (prefers-reduced-motion: reduce) { *, *::before, *::after { transition: none !important; animation: none !important } }`.

Numbers that are read as quantities — stat tiles, table numeric cells — get `font-variant-numeric: tabular-nums`.

**Declare `@font-face` in `app.css` only**, never inside a component: a `@font-face` inside a shadow root is ignored by some engines and duplicated by others.

- [ ] **Step 4: Write the shell**

Replace `wwwroot/index.html` with the real shell: `<nav aria-label="Pages">` holding `<a href="#scan">` and `<a href="#history">` with `aria-current="page"` on the active one, and a `<main>` with one `<section>` per page, hidden except the active one. A `<script type="module" src="/app.js"></script>` at the end.

The sidebar footer shows last-scan and kept-scan counts, filled in Task 7; leave the elements present and empty.

- [ ] **Step 5: Write the router in `app.js`**

Hash routing only: `location.hash` → show one `<section>`, set `aria-current`, and dispatch a `page-shown` CustomEvent the pages listen for. Default to `#scan`. Nothing else in `app.js` yet — Task 4 adds the bridge client.

- [ ] **Step 6: Verification checkpoint**

Run: `dotnet build NDSTK.CookieScanner.Desktop/NDSTK.CookieScanner.Desktop.csproj` — succeeded.
Run: `dotnet test NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj` — the "every asset the index references resolves" test now covers `app.css`, `app.js`, and the font; all passing. **If this test fails, a filename is wrong — fix the filename, never the test.**

Launch and screenshot as in Task 2, to `task3-shell.png`. The screenshot must show: the sidebar with both links and Scan active, Inter rendering (not Segoe UI — check the lowercase `g`), the cool canvas, and an empty page area. Report whether the font loaded.

Run: `git status --short` — the five files of this task.

---

## Task 4: The bridge, and a scan that runs

**Files:**
- Create: `DashboardMessages.cs`, `DashboardBridge.cs`, `ScanSession.cs`, `WebViewScanLog.cs`, `DashboardSettings.cs`, `wwwroot/components/cs-log-panel.js`
- Modify: `DashboardForm.cs`, `wwwroot/index.html`, `wwwroot/app.js`
- Test: `NDSTK.Desktop.Tests/DashboardMessageTests.cs`

**Interfaces:**
- Consumes: `DashboardAssets` (Task 2); `ScanRunner`, `ScanOptions`, `ScanResult`, `IScanLog`, `CatalogueSource`, `ScanReportWriter`, `ScanHistory` from `NDSTK.CookieScanner`.
- Produces: the message envelope in both directions (below); `DashboardBridge.Post(object envelope)`; `ScanSession.StartAsync(RunCommand)` and `ScanSession.Cancel()`. Tasks 5–8 add message types and pages, never new transports.

- [ ] **Step 1: Write the failing protocol test**

`NDSTK.Desktop.Tests/DashboardMessageTests.cs`:

```csharp
using NDSTK.CookieScanner.Desktop;

namespace NDSTK.Desktop.Tests;

public class DashboardMessageTests
{
    [Fact]
    public void A_run_command_parses_with_all_its_options()
    {
        const string json = """
            {"type":"run","url":"https://localhost:44351","maxPages":7,"locale":"En",
             "memberEmail":"a@b.c","memberPassword":"secret","clientId":"cookie-scanner","dryRun":false}
            """;

        DashboardCommand? command = DashboardCommand.Parse(json);

        RunCommand run = Assert.IsType<RunCommand>(command);

        Assert.Equal("https://localhost:44351", run.Url);
        Assert.Equal(7, run.MaxPages);
        Assert.Equal("En", run.Locale);
        Assert.Equal("secret", run.MemberPassword);
        Assert.False(run.DryRun);
    }

    [Fact]
    public void A_cancel_command_parses()
    {
        Assert.IsType<CancelCommand>(DashboardCommand.Parse("""{"type":"cancel"}"""));
    }

    // The page is inside the exe, so an unknown type is a bug rather than an attack - but throwing
    // here would take down the message loop, and a dropped message is the smaller failure.
    [Fact]
    public void An_unknown_type_is_ignored_rather_than_throwing()
    {
        Assert.Null(DashboardCommand.Parse("""{"type":"launch-missiles"}"""));
    }

    [Fact]
    public void Malformed_json_is_ignored_rather_than_throwing()
    {
        Assert.Null(DashboardCommand.Parse("not json"));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj --filter DashboardMessageTests`
Expected: `CS0246: The type or namespace name 'DashboardCommand' could not be found`.

- [ ] **Step 3: Write `DashboardMessages.cs`**

An abstract `DashboardCommand` with `static DashboardCommand? Parse(string json)` that reads the `type` discriminator and deserialises the matching record, returning null for an unknown type or malformed JSON. The records:

```csharp
public sealed record RunCommand(
    string Url, int MaxPages, string Locale, string? MemberEmail,
    string? MemberPassword, string? ClientId, bool DryRun) : DashboardCommand;

public sealed record CancelCommand : DashboardCommand;
public sealed record ListHistoryCommand : DashboardCommand;
public sealed record LoadScanCommand(string Path) : DashboardCommand;
public sealed record CompareCommand(string PathA, string PathB) : DashboardCommand;
public sealed record OpenReportFolderCommand : DashboardCommand;
public sealed record ReadyCommand : DashboardCommand;
```

Parse with `ScanJson.Options` — the same camelCase, enum-as-name options the report file uses — so the page's JSON dialect is identical in both directions and there is one place to change it.

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj --filter DashboardMessageTests`
Expected: 4 passing.

- [ ] **Step 5: Write `WebViewScanLog`**

```csharp
namespace NDSTK.CookieScanner.Desktop;

/// <summary>
/// The dashboard's log: every line the scan emits, posted into the page.
/// </summary>
/// <remarks>
/// Every write marshals to the UI thread. The scan runs on a background task and the engine logs from
/// Playwright's own threads, so posting directly would throw - and would do it on a failure path that
/// is rarely exercised, which is the worst place to discover it.
/// </remarks>
public sealed class WebViewScanLog(DashboardBridge bridge) : IScanLog
{
    public void Info(string message) => bridge.Post(new { type = "log", level = "info", message });

    public void Warning(string message) => bridge.Post(new { type = "log", level = "warning", message });
}
```

The marshalling lives in `DashboardBridge.Post`, so every sender gets it rather than only this one.

- [ ] **Step 6: Write `DashboardBridge`**

Responsibilities, and only these:

- `Post(object envelope)`: serialise with `ScanJson.Options` on the calling thread, then marshal. If the page has not sent `ready` yet, enqueue into a bounded queue (cap 500, drop oldest) instead — **messages posted before the page loads are silently dropped by WebView2**, and the first scan's opening lines would vanish. On `ready`, flush in order.
- Marshalling: guard `IsHandleCreated`/`IsDisposed`, `BeginInvoke`, and wrap the `PostWebMessageAsJson` call in `try`/`catch (ObjectDisposedException)` — the guard is not atomic with the post, so closing the window between them throws.
- `WebMessageReceived` → `DashboardCommand.Parse` → raise a typed event the form handles. A null parse is ignored.

- [ ] **Step 7: Write `DashboardSettings`**

Port `NDSTK.CookieScanner.Gui/GuiSettings.cs` **unchanged in behaviour**: same file at `%LOCALAPPDATA%\NDSTK.CookieScanner\settings.json`, the same six fields with the same names and defaults (`Url`, `MaxPages`, `Locale`, `MemberEmail`, `ClientId`, `DryRun = true`), the same shared `JsonSerializerOptions` with `JsonStringEnumConverter`, and both `try`/`catch (Exception)` blocks with their comments intact. Rename the type to `DashboardSettings` and the namespace; **change nothing else**, so a user's existing settings file loads after the swap.

Copy the class comment verbatim: the secret and the member password are deliberately absent and must stay absent.

- [ ] **Step 8: Write `ScanSession`**

Owns one run and nothing else:

- Builds `ScanOptions` from a `RunCommand`: `Target` = the same URL as `Url`; `ClientSecret` from `Environment.GetEnvironmentVariable(ScanOptions.SecretVariable)`; `ReportDir` = `%LOCALAPPDATA%\NDSTK.CookieScanner\reports`; `Headed` = false. Throw `ArgumentException` with a clear message when the URL is not absolute, reusing the CLI's rule rather than inventing a second one.
- `Task.Run(() => new ScanRunner(options, () => CatalogueSource.Load(log), log).RunAsync(token))`, with a `CancellationTokenSource` per run.
- On success: post `result`, then **write the report and the history entry in two independent `try`/`catch (IOException or UnauthorizedAccessException)` blocks**, each posting a `log` warning on failure. Independent because a locked report file must not cost the history entry, and neither must cost the result the page already has.
- `catch (OperationCanceledException)` **before** `catch (Exception)`, posting a `log` warning that the scan was cancelled and no report was written.
- `finally`: post `state { running: false }`, dispose the CTS, null it.

- [ ] **Step 9: Wire the form and write the log panel**

`DashboardForm` creates the bridge after `EnsureCoreWebView2Async`, handles its command event, and routes `run`/`cancel` to a `ScanSession`.

`wwwroot/components/cs-log-panel.js` — a Lit element that appends `<li>` nodes rather than re-rendering the list per line, batching arrivals with `requestAnimationFrame`; `role="log" aria-live="polite" aria-relevant="additions"`; warnings get the `--log-warn` colour **and** a leading `Warning` word, so the level is never carried by colour alone; auto-scroll only when already scrolled to the bottom, so reading scrollback mid-scan is not fought; `user-select: text`.

Add to the Scan section of `index.html`: the URL input, an `<details>` holding max pages, locale, member email, member password (`<input type="password" autocomplete="off">`) and client id, a Dry run checkbox, Run and Cancel buttons, the secret-status line, and `<cs-log-panel>`.

In `app.js`: send `ready` on load; `postMessage` for `run`/`cancel`; render `log` and `state` messages; disable the inputs and swap Run for Cancel while running.

The secret-status line is filled from a `state` message the host posts on `ready`, carrying whether `NDSTK_COOKIESCAN_CLIENT_SECRET` is set — read once at startup.

- [ ] **Step 10: Verification checkpoint**

Run all three builds and both test projects — green.

**Then, with the site up**, launch the window and run one scan by driving it through UI Automation (the page's DOM is exposed to UIA through WebView2, so buttons are reachable by name). Capture the log text partway through, and again at the end. Report:

- the log filled progressively, not all at once;
- warnings rendered as warnings;
- `%LOCALAPPDATA%\NDSTK.CookieScanner\reports` gained both report files;
- `…\scans` gained exactly one entry;
- a second run, cancelled mid-scan, left **no** new file in either folder — list both before and after.

Screenshot the running state to `task4-running.png`, window rectangle only.

Run: `git status --short` — the nine files of this task.

---

## Task 5: Findings — tiles and the table

**Files:**
- Create: `wwwroot/components/cs-stat-tile.js`, `wwwroot/components/cs-findings-table.js`
- Modify: `wwwroot/index.html`, `wwwroot/app.js`, `wwwroot/app.css`

**Interfaces:**
- Consumes: the `result` envelope (Task 4), whose `scan` payload is a serialized `ScanResult` — `candidates`, `violations`, `expectedButNotObserved`, `hostsByPass`, `outcome`, `canReachApi`, `dryRun`, `completedAt`, `site`, `options`.
- Produces: `<cs-stat-tile>` and `<cs-findings-table>`, both reused by the History page in Task 7.

- [ ] **Step 1: Write `<cs-stat-tile>`**

Properties: `value` (number), `label`, `hint`, `tone` (`"blue" | "red" | "amber" | "teal"`). Renders the number at 26px/600 with `tabular-nums`, the label at 11px/500, the hint at 10px in `--ink-500`, on the tone's tint. No logic beyond that.

- [ ] **Step 2: Write `<cs-findings-table>`**

One property, `result` (the parsed scan). Renders a real `<table>` with `<th scope="col">`: Name, Storage, Category, First seen in, Duration, and a state column.

**The colouring rule, which is the one thing in this component that must not be improvised:**

```js
    // A row is red when the cookie is a violation, which is NOT the same as its flag being one.
    // candidates is the earliest-per-name reduction; violations is computed over the raw
    // observations, deliberately, because a violation is a property of one sighting. A cookie first
    // set in a pass that granted its category and set again in one that did not is a violation the
    // flag knows nothing about - colouring by flag alone would leave the window disagreeing with the
    // exit code CI gates on.
    const violations = new Set(result.violations.map(v => v.name.toLowerCase()));
    const isViolation = c => c.flag === 'Violation' || violations.has(c.name.toLowerCase());
```

A violation row gets `--red-50` and a `Violation` pill; `flag === 'NeedsReview'` gets `--amber-50` and a `Needs review` pill. **The state is carried by the pill's text as well as the colour**, so it survives a monochrome screenshot and a screen reader.

Cookie names render in `--font-mono`.

- [ ] **Step 3: Fill the tiles**

Four, in this order, from the result: entries found (`candidates.length`, blue); violations (`violations.length`, red); needs review (`candidates.filter(c => c.flag === 'NeedsReview').length`, amber); expected but not observed (`expectedButNotObserved.length`, teal).

Hints under each: "N added last run" comes from `outcome?.added?.length` when `outcome` is present, otherwise omit the hint; violations reads "fails the run · exit 1" when non-zero and "none" when zero.

- [ ] **Step 4: Append the summary lines**

On `result`, append `ScanReportWriter.SummaryLines(options, result)` to the log — the host posts them in the `result` envelope as `summary: string[]`. The counts and the two report paths then appear in the log exactly as the CLI prints them, without the page reformatting anything.

- [ ] **Step 5: Verification checkpoint**

Builds and both suites green.

**With the site up**, run one scan through the window and check by UI Automation and by screenshot (`task5-findings.png`):

- the tile numbers match the log's "N entr(ies) found." line;
- the table's row count matches `candidates.length` in the report JSON on disk;
- every red row is in the union of `flag === 'Violation'` and the `violations` names — **the union, not just the flag**;
- the summary lines appear in the log with the json path aligned under the markdown path.

Run: `git status --short` — the five files of this task.

---

## Task 6: The trend

**Files:**
- Create: `wwwroot/components/cs-trend-chart.js`
- Modify: `wwwroot/index.html`, `wwwroot/app.js`, `DashboardBridge.cs` or the form's command handling (a `history` message on `ready`)

**Interfaces:**
- Consumes: `ScanHistory.List()` and `ScanHistoryEntry.ViolationCount` (Task 1); the `history` envelope.
- Produces: `<cs-trend-chart>`, reused nowhere else but sized by the Scan page's hero.

- [ ] **Step 1: Post the history**

The host answers `listHistory` with `history { entries: [...] }`, each entry `{ path, completedAt, site, entryCount, violationCount, exitCode }`. The page requests it on `ready` and again after every completed scan.

- [ ] **Step 2: Write `<cs-trend-chart>`**

Inline SVG, `viewBox="0 0 320 78"`, `preserveAspectRatio="none"`, two `<path>` series — entries in `--blue-600` over a gradient area fill, violations in `--red-600` — with `vector-effect: non-scaling-stroke` so the non-uniform scale does not distort the stroke, an end-point dot on each, and the latest entry count pulled out at 27px beside it.

X is scan index, not time: scans are irregular, and a time axis would put a cluster of Tuesday's runs on top of each other. Y is `max(entryCount)` with 10% headroom. Only the scans for the site currently in the URL field, most recent 20.

`role="img"` with an `aria-label` naming the series, the count and the range, plus a visually-hidden `<table>` carrying the numbers — a chart that only exists as a shape is unreadable to half its potential audience.

Empty history renders "No scans yet" rather than an empty axis.

- [ ] **Step 3: Verification checkpoint**

Builds and both suites green.

**With the site up**, run two scans a minute apart and screenshot (`task6-trend.png`). The chart must show one more point after the second, the entries line must move, and the violations line must sit at zero along the bottom without disappearing. Report the point count against the number of files in `…\scans` for that site.

Run: `git status --short` — the four files of this task.

---

## Task 7: History — list and view one

**Files:**
- Create: `wwwroot/components/cs-history-list.js`
- Modify: `wwwroot/index.html`, `wwwroot/app.js`, the form's command handling

**Interfaces:**
- Consumes: `history` (Task 6), `ScanHistory.Load` via a new `loadScan` command answering with `scan { result }`.
- Produces: `<cs-history-list>` with a `selection-changed` event carrying the selected paths. Task 8 adds Compare to the same component.

- [ ] **Step 1: Write `<cs-history-list>`**

Columns: Completed, Site, Entries, Result. The result column is a **word, not a number** — `exitCode === 1` → a red `1 violation` pill (or `N violations` from `violationCount`), `exitCode === 2` → an amber `write-back failed` pill, `0` → a teal `clean` pill — with the numeric exit code in the row's `title` attribute. Newest first, as `List()` returns them.

Rows are selectable, multi-select with a checkbox per row so selection is explicit rather than modifier-dependent.

- [ ] **Step 2: Show one**

Selecting exactly one sends `loadScan { path }`; the host answers `scan { result }` and the page renders it in **the same `<cs-findings-table>` the Scan page uses**. Do not write a second table: the colouring rule must exist once.

A `null` from `ScanHistory.Load` — a file deleted or corrupted between listing and loading — comes back as `error { message }` and the page shows it inline, not silently.

- [ ] **Step 3: Fill the sidebar footer**

Last scan's time and entry count, and "N of 50 kept", from the same `history` message.

- [ ] **Step 4: Verification checkpoint**

Builds and both suites green.

**With the site up**, drive the History page by UI Automation and screenshot (`task7-history.png`):

- the list's entry count equals the number of parseable files in `…\scans`;
- selecting one fills the table, and its row count matches that scan's own JSON on disk;
- the result words match each file's computed exit code;
- deliberately corrupt one history file (write `{}` into a copy — **the shape check makes this safe**), reload, and confirm the list still renders the rest and the count drops by one. Restore the file afterwards and say so.

Run: `git status --short` — the four files of this task.

---

## Task 8: Compare

**Files:**
- Create: `wwwroot/components/cs-diff-view.js`
- Modify: `wwwroot/components/cs-history-list.js`, `wwwroot/app.js`, the form's command handling

**Interfaces:**
- Consumes: `ScanDiff.Between(older.Candidates, newer.Candidates)` and `CategoryChange` from `NDSTK.CookieScan.Core`; `ScanOptionsSummary` (Task 1).
- Produces: nothing further.

- [ ] **Step 1: Answer `compare`**

The host loads both results, **orders the pair by `CompletedAt`** — not by click order, so "appeared" always means "in the newer one" — calls `ScanDiff.Between(older.Candidates, newer.Candidates)`, and posts:

```
diff {
  older: { completedAt, site, entryCount },
  newer: { completedAt, site, entryCount },
  appeared: [candidate], disappeared: [candidate],
  recategorised: [{ name, from, to }],
  optionsKnown: bool,      // both results carry an Options summary
  optionsDiffer: bool,     // and they differ in any recorded field
  siteDiffers: bool
}
```

Either result failing to load answers `error { message: "One of those scans could not be read." }`.

- [ ] **Step 2: Write `<cs-diff-view>`**

Three labelled groups with counts — Appeared (teal), Disappeared (red), Recategorised (blue). An empty group collapses to one sentence ("Nothing disappeared."), and all three empty renders **"Nothing changed between these two scans"** rather than three empty grids. A header names both scans by completion time.

**The warning banner is the point of Task 1.** When `optionsDiffer`, show an amber banner naming what differed — "these scans ran with different options: member sign-in on vs off" — because that difference explains findings that look like site changes. When `optionsKnown` is false, the banner instead says the options were not recorded for one of these scans, which is honest about an older file rather than claiming they matched. When `siteDiffers`, say so: the diff is between two different sites and almost every row will be noise.

- [ ] **Step 3: Verification checkpoint**

Builds and both suites green.

**With the site up**, seed two genuinely different scans from the CLI — one plain, one with `--member-email 'cookie-scan-test@ndstk.local' --member-password '<test-member-password>'`, a minute apart — then drive Compare by UI Automation and screenshot (`task8-compare.png`):

- the member-vs-public pair shows exactly `.AspNetCore.Identity.Application` in one group; name the group;
- selecting the two in the **opposite order** puts it in the **same** group;
- the options banner appears and names the member difference;
- comparing two identical scans shows the nothing-changed sentence, legibly, not clipped;
- selecting one entry while a diff is showing returns the pane to the detail table.

Run: `git status --short` — the four files of this task.

---

## Task 9: Retire, publish, verify, document

**Files:**
- Delete: `NDSTK.CookieScanner.Gui/**`
- Modify: `NDSTK.slnx`, `NDSTK.csproj`, `docs/cookie-scanner.md`

**Interfaces:** none.

The window is deleted **last**, after the dashboard has been shown doing everything it did. Nothing in this task changes behaviour.

- [ ] **Step 1: The output gate**

The engine gained two record fields in Task 1; the console tool must not have noticed. Build the pre-change CLI from a worktree pinned at this branch's base and run both against the same site, minutes apart:

```bash
BASE=$(git merge-base master HEAD)
git worktree add /tmp/ndstk-base "$BASE"
dotnet build /tmp/ndstk-base/NDSTK.CookieScanner/NDSTK.CookieScanner.csproj
```

Then, for each of the four scenarios — public, member, refused connection, bad credentials — run the base build and this build with the same arguments 75 seconds apart and diff **stdout, stderr and `cookie-scan-report.md`**. The previous plan's harness is the model; write it as a script and report every diff verbatim.

**Every diff must be empty and the exit codes must match.** `cookie-scan-report.json` differs by the new `options` key and is not compared. The client secret comes from `appsettings.Secrets.json` at `NDSTK:CookieScanApiUser:ClientSecret`; read it in the script, never into a report.

Remove the worktree afterwards.

- [ ] **Step 2: Delete the window**

Delete the `NDSTK.CookieScanner.Gui` directory, its `NDSTK.slnx` entry, and `;NDSTK.CookieScanner.Gui\**` from `NDSTK.csproj`'s `DefaultItemExcludes`.

Run: `grep -rn "CookieScanner.Gui\|GuiSettings" --include=*.cs --include=*.csproj --include=*.slnx --include=*.md . | grep -v "/obj/\|/bin/\|docs/superpowers/"` — every remaining hit is a documentation reference to fix in Step 4, or a mistake.

- [ ] **Step 3: Publish both exes and prove the window runs standalone**

```bash
dotnet publish NDSTK.CookieScanner -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
dotnet publish NDSTK.CookieScanner.Desktop -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

Report both names and sizes; compression is expected to roughly halve them against the previous ~213 MB.

Then **copy `dist/ndstk-cookiescan-ui.exe` to a directory outside the repository and run it from there**, completing one scan. This is the check that caught the CLI's missing Playwright assets once, and it now also proves the embedded dashboard and the redirected user-data folder.

Then **copy it to a read-only location** — a folder under `C:\Program Files`, created with an admin prompt if needed, or any folder with write permission removed — and launch it once. It must open. This is the case the default user-data folder fails, and the only way to know the redirect works.

Report both, and delete the copies.

- [ ] **Step 4: Update the documentation**

In `docs/cookie-scanner.md`: the window is now the dashboard; the WinForms window is gone. Update the two-executables section, the publish commands (including `EnableCompressionInSingleFile`), and add: where the WebView2 user-data folder lives and why it is not beside the exe; that the WebView2 Evergreen runtime is a prerequisite and what the window says when it is missing; that the report JSON gained an `options` object; and that first launch extracts ~200 MB to `%TEMP%\.net`, with `DOTNET_BUNDLE_EXTRACT_BASE_DIR` named as the escape hatch.

Update "What has been verified" to cover the dashboard, and move nothing into it that Step 5 has not actually done.

- [ ] **Step 5: Final verification checkpoint**

Run: `dotnet test NDSTK.Tests/NDSTK.Tests.csproj` — green.
Run: `dotnet test NDSTK.Desktop.Tests/NDSTK.Desktop.Tests.csproj` — green.
Run: with the site **stopped**, `dotnet build NDSTK.slnx` — succeeded, 0 errors, all six projects. Hand the site back rather than restarting it.
Run: `git status --short` — the deletions plus three modified files. `dist/` must not appear.

---

## Plan Self-Review

**1. Spec coverage.** Every section maps to a task:

| Spec section | Task |
| --- | --- |
| WebView2 in a WinForms shell, runtime check, hardening | 2 |
| User-data folder under `%LOCALAPPDATA%` | 2, verified 9 |
| Embedded assets over `https://app.localhost/` | 2 |
| The bridge, envelope both ways, buffer until `ready` | 4 |
| Threading: marshal every log write | 4 |
| Cancellation semantics | 4 |
| Settings ported; credentials never persisted | 4 |
| Design tokens, fonts, no build step, Lit vendored | 3 |
| Scan page: run card, tiles, findings table | 4, 5 |
| The violation colouring union rule | 5 |
| Trend across history | 1 (`ViolationCount`), 6 |
| History: list, view one | 7 |
| Compare, ordered by time, options warning | 1 (`Options`), 8 |
| Two additive record fields | 1 |
| Retiring the WinForms window | 9 |
| Publish, standalone, read-only location | 9 |
| Testing: assets, protocol, record round-trips | 1, 2, 4 |
| Verification 1–6 | 1, 4, 5, 6, 7, 8, 9 |

No gaps.

**2. Placeholder scan.** No "TBD", no "add error handling", no "similar to Task N". Task 3's CSS is described by its parts rather than printed in full, which is a deliberate limit: the tokens that must not be improvised are given verbatim, and the layout rules built from them are ordinary CSS.

**3. Type consistency.** Cross-checked producer against consumer: `ScanOptionsSummary` and `ScanHistoryEntry.ViolationCount` (1 → 6, 7, 8); `DashboardAssets.TryOpen` (2 → 2's handler, 3's test); `DashboardCommand` and its records (4 → 5–8); `DashboardBridge.Post` (4 → all); `<cs-findings-table>` (5 → 7); `<cs-stat-tile>` (5); `<cs-history-list>` selection event (7 → 8); `ScanDiff.Between(older, newer)` taking candidate lists (8, matching Core's existing signature).

One inconsistency found and fixed while reviewing: Task 7 originally described its own results table, which would have put the violation colouring rule in two places — the exact defect the last plan's Task 8 review caught. It now reuses `<cs-findings-table>`.
