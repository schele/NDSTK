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

        await ConsentDecision.RecordAsync(context, options.Url, endpointPath, pass);

        Dictionary<(string Name, StorageKind Storage), PassEntry> found = [];

        foreach (Uri url in urls)
        {
            PageObservation observation = await PageCapture.VisitAsync(page, url, hosts);

            foreach (CapturedEntry entry in observation.Entries)
            {
                // First URL wins - it is the page that actually caused the thing to be set, and
                // that is what makes a report line actionable.
                // Keyed on the lowercased name so this dedup agrees with
                // ObservedEntries.EarliestPerName's case-insensitive grouping - the stored entry
                // still carries the original casing, only the key is normalised.
                found.TryAdd(
                    (entry.Name.ToLowerInvariant(), entry.Storage),
                    new PassEntry(entry.Name, entry.Storage, entry.Expires, url));
            }
        }

        return new PassResult(pass, [.. found.Values], hosts);
    }
}
