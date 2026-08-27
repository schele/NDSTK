using System.Text;
using System.Text.Json;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner;

/// <summary>What the merge endpoint reported back, or null when it was never called.</summary>
public sealed record MergeOutcome(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> AlreadyDeclared,
    IReadOnlyList<string> DeclaredButNotFound,
    Guid PolicyPageKey,
    bool Saved);

/// <summary>
/// Writes the console summary and the two report files, and decides the process exit code.
/// </summary>
public static class ScanReportWriter
{
    public const int ExitClean = 0;
    public const int ExitViolations = 1;
    public const int ExitError = 2;

    /// <remarks>
    /// <paramref name="violations"/> is passed in rather than filtered out of
    /// <paramref name="candidates"/>, because a violation is a property of one sighting while a
    /// candidate is the earliest-per-name reduction. Deriving them from the reduced list would miss
    /// a cookie whose category WAS granted in the pass that first set it and which was then set
    /// again in a pass that granted something else - see <c>ViolationScan.Find</c>.
    /// </remarks>
    public static int Write(
        ScanOptions options,
        IReadOnlyList<CookieDeclarationCandidate> candidates,
        IReadOnlyList<CookieDeclarationCandidate> violations,
        IReadOnlyList<string> expectedButNotObserved,
        IReadOnlyDictionary<ConsentPass, IReadOnlySet<string>> hostsByPass,
        MergeOutcome? outcome)
    {
        List<CookieDeclarationCandidate> needsReview =
            [.. candidates.Where(candidate => candidate.Flag == CandidateFlag.NeedsReview)];

        var markdown = new StringBuilder();

        markdown.AppendLine("# Cookie scan report");
        markdown.AppendLine();
        markdown.AppendLine($"- Site: {options.Url}");
        markdown.AppendLine($"- Pages per pass: up to {options.MaxPages}");
        markdown.AppendLine($"- Member dimension: {(options.MemberScanEnabled ? "yes" : "no")}");
        markdown.AppendLine($"- Write-back: {Describe(options, outcome, candidates.Count)}");
        markdown.AppendLine();

        // Violations first, deliberately. It is the finding that matters, and burying it under a
        // table of forty ordinary cookies is how a compliance problem goes unread.
        Section(markdown, "Violations", violations.Select(candidate =>
            $"**{candidate.Name}** — categorised `{candidate.Category}`, but was set during the "
            + $"`{candidate.FirstSeenPass}` pass, which did not grant it. First seen at {candidate.FirstSeenUrl}"));

        if (outcome is not null)
        {
            // In a dry run nothing was actually added - Describe gets that right already, but this
            // heading used to claim otherwise regardless of DryRun.
            string addedHeading = options.DryRun ? "Would be added (dry run)" : "Added to the policy page (draft)";
            Section(markdown, addedHeading, outcome.Added);
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
                    $"  The policy page ({outcome.PolicyPageKey}) was saved as a DRAFT. Review the "
                    + "new blocks in the backoffice and publish when you are happy with the wording.");
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

        // Violations outrank everything: they are the finding this tool exists to produce.
        if (violations.Count > 0)
        {
            return ExitViolations;
        }

        // A missing credential is not an error - report-only is a supported mode. But a write-back
        // that was configured, had something to write, and then failed IS one, and it must not
        // exit 0: a CI job gating on this would otherwise stay green while the policy page
        // silently stopped being updated. The candidates.Count check matters because Program
        // deliberately skips calling the merge endpoint for an empty candidate list (see its own
        // comment) - that is a legitimate outcome, not an unattempted or failed write-back, and
        // must not be reported as one.
        return outcome is null && options.CanReachApi && candidates.Count > 0 ? ExitError : ExitClean;
    }

    private static string Describe(ScanOptions options, MergeOutcome? outcome, int candidateCount)
        => outcome switch
        {
            null when options.CanReachApi is false => "not configured (report only)",
            // Program deliberately skips the merge call for an empty candidate list rather than
            // let the endpoint reject it - that is a legitimate outcome, not an attempt that failed.
            null when candidateCount == 0 => "not attempted - nothing found to write back",
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
