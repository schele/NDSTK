using System.Text;
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
/// Writes the two report files and formats the console summary. Split into two independent jobs
/// so the window can take the same <see cref="ScanResult"/> and show its findings in a grid
/// without also getting a console-formatted duplicate of the summary text.
/// </summary>
/// <remarks>
/// The exit code itself is not decided here - it lives on <see cref="ScanResult.ExitCode"/>, so
/// both front ends see the same number without either one recomputing it.
/// </remarks>
public static class ScanReportWriter
{
    public const int ExitError = 2;

    /// <summary>
    /// Creates the report directory and writes <c>cookie-scan-report.md</c> and
    /// <c>cookie-scan-report.json</c>.
    /// </summary>
    /// <remarks>
    /// Sources every section from <paramref name="result"/> rather than from separate parameters,
    /// so a caller cannot pass mismatched pieces of two different scans.
    /// </remarks>
    public static void WriteFiles(ScanOptions options, ScanResult result)
    {
        var markdown = new StringBuilder();

        markdown.AppendLine("# Cookie scan report");
        markdown.AppendLine();
        markdown.AppendLine($"- Site: {options.Url}");
        markdown.AppendLine($"- Pages per pass: up to {options.MaxPages}");
        markdown.AppendLine($"- Member dimension: {(options.MemberScanEnabled ? "yes" : "no")}");
        markdown.AppendLine($"- Write-back: {Describe(options, result.Outcome, result.Candidates.Count)}");
        markdown.AppendLine();

        // Violations first, deliberately. It is the finding that matters, and burying it under a
        // table of forty ordinary cookies is how a compliance problem goes unread.
        Section(markdown, "Violations", result.Violations.Select(candidate =>
            $"**{candidate.Name}** — categorised `{candidate.Category}`, but was set during the "
            + $"`{candidate.FirstSeenPass}` pass, which did not grant it. First seen at {candidate.FirstSeenUrl}"));

        if (result.Outcome is not null)
        {
            // In a dry run nothing was actually added - Describe gets that right already, but this
            // heading used to claim otherwise regardless of DryRun.
            string addedHeading = options.DryRun ? "Would be added (dry run)" : "Added to the policy page (draft)";
            Section(markdown, addedHeading, result.Outcome.Added);
            Section(markdown, "Already declared", result.Outcome.AlreadyDeclared);
            Section(
                markdown,
                "Declared but not found — reported, never deleted",
                result.Outcome.DeclaredButNotFound);
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

        Section(markdown, "Needs review — only ever seen with everything granted", result.NeedsReview.Select(
            candidate => $"{candidate.Name} — written as `{candidate.Category}`, which is a fallback"));

        Section(markdown, "Expected but not observed", result.ExpectedButNotObserved);

        markdown.AppendLine("## All entries found");
        markdown.AppendLine();
        markdown.AppendLine("| Name | Storage | Category | First seen in | Duration |");
        markdown.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (CookieDeclarationCandidate candidate in result.Candidates)
        {
            markdown.AppendLine(
                $"| `{candidate.Name}` | {candidate.StorageType} | {candidate.Category} "
                + $"| {candidate.FirstSeenPass} | {candidate.Duration} |");
        }

        markdown.AppendLine();

        Section(markdown, "Third-party hosts contacted", result.HostsByPass
            .Where(pass => pass.Value.Count > 0)
            .Select(pass => $"{pass.Key}: {string.Join(", ", pass.Value.Order())}"));

        Directory.CreateDirectory(options.ReportDir);

        (string markdownPath, string jsonPath) = ReportPaths(options);

        File.WriteAllText(markdownPath, markdown.ToString());
        File.WriteAllText(jsonPath, ScanJson.Serialize(result));
    }

    /// <summary>
    /// The lines the console summary prints, in order, blank lines included as empty strings so
    /// the caller's line-by-line printing reproduces the pre-refactor output exactly.
    /// </summary>
    /// <remarks>
    /// Takes <paramref name="options"/> as well as <paramref name="result"/> because its final two
    /// lines name the report paths, and those depend on <see cref="ScanOptions.ReportDir"/> - a
    /// <see cref="ScanResult"/> alone cannot produce them.
    /// </remarks>
    public static IReadOnlyList<string> SummaryLines(ScanOptions options, ScanResult result)
    {
        (string markdownPath, string jsonPath) = ReportPaths(options);

        List<string> lines =
        [
            "",
            $"{result.Candidates.Count} entr(ies) found.",
        ];

        if (result.Violations.Count > 0)
        {
            lines.Add("");
            lines.Add($"  {result.Violations.Count} CONSENT VIOLATION(S):");

            foreach (CookieDeclarationCandidate violation in result.Violations)
            {
                lines.Add(
                    $"    {violation.Name} ({violation.Category}) was set during the "
                    + $"{violation.FirstSeenPass} pass, which did not grant it.");
            }
        }

        if (result.Outcome is not null)
        {
            lines.Add("");
            lines.Add(
                $"  {result.Outcome.Added.Count} added, {result.Outcome.AlreadyDeclared.Count} already declared, "
                + $"{result.Outcome.DeclaredButNotFound.Count} declared but not found.");

            if (result.Outcome.Saved)
            {
                lines.Add(
                    $"  The policy page ({result.Outcome.PolicyPageKey}) was saved as a DRAFT. Review the "
                    + "new blocks in the backoffice and publish when you are happy with the wording.");
            }
        }

        if (result.ExpectedButNotObserved.Count > 0)
        {
            lines.Add("");
            lines.Add("  Expected but not observed: " + string.Join(", ", result.ExpectedButNotObserved));
        }

        lines.Add("");
        lines.Add($"Report written to {markdownPath}");
        lines.Add($"                  {jsonPath}");

        return lines;
    }

    // Both report files live beside each other, named from the same options - computed once so
    // WriteFiles and SummaryLines can never disagree about where the files went.
    private static (string MarkdownPath, string JsonPath) ReportPaths(ScanOptions options)
        => (Path.Combine(options.ReportDir, "cookie-scan-report.md"),
            Path.Combine(options.ReportDir, "cookie-scan-report.json"));

    private static string Describe(ScanOptions options, MergeOutcome? outcome, int candidateCount)
        => outcome switch
        {
            null when options.CanReachApi is false => "not configured (report only)",
            // The scan deliberately skips the merge call for an empty candidate list rather than
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
