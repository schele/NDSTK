using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

namespace NDSTK.CookieScanner.Gui;

/// <summary>
/// The Scan tab: the options one run needs, the engine's live commentary, and what it found.
/// </summary>
public sealed partial class MainForm
{
    // Named, all of them. A Name becomes the AutomationId, without which automation can only find
    // a control by type and index, and every rename of a neighbour breaks it. AccessibleName is
    // set separately, below: WinForms does not associate a Label with the control beside it, so an
    // unnamed text box is announced by its contents instead of by what it is for.
    private readonly TextBox urlBox = new() { Name = "urlBox" };
    private readonly NumericUpDown maxPages = new() { Name = "maxPages" };
    private readonly ComboBox localeBox = new() { Name = "localeBox" };
    private readonly TextBox memberEmailBox = new() { Name = "memberEmailBox" };
    private readonly TextBox memberPasswordBox = new() { Name = "memberPasswordBox" };
    private readonly TextBox clientIdBox = new() { Name = "clientIdBox" };
    private readonly Label secretStatus = new() { Name = "secretStatus" };
    private readonly CheckBox dryRun = new() { Name = "dryRun" };
    private readonly Button run = new() { Name = "run" };
    private readonly Button cancel = new() { Name = "cancel" };
    private readonly SplitContainer findingsSplit = new() { Name = "findingsSplit" };
    private readonly RichTextBox log = new() { Name = "log", AccessibleName = "Scan log" };
    private readonly DataGridView findings = new() { Name = "findings", AccessibleName = "Findings" };

    private readonly GuiSettings settings = GuiSettings.Load();

    private CancellationTokenSource? cancellation;

    /// <summary>
    /// Where the window writes its report files.
    /// </summary>
    /// <remarks>
    /// Not the current directory, which is what the console tool defaults to. A window's current
    /// directory is wherever it happened to be launched from - a desktop shortcut leaves it at
    /// the system directory - so reports would scatter or fail to write. The last two lines of
    /// <see cref="ScanReportWriter.SummaryLines"/> name both files and land in the log pane, so
    /// the operator is still told where they went.
    /// </remarks>
    private static string ReportDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NDSTK.CookieScanner",
        "reports");

    /// <summary>Everything a running scan must not let the operator change under it.</summary>
    private Control[] Inputs =>
        [urlBox, maxPages, localeBox, memberEmailBox, memberPasswordBox, clientIdBox, dryRun, run];

    private Locale SelectedLocale => localeBox.SelectedItem is Locale locale ? locale : Locale.Sv;

    private void BuildScanTab(TabPage page)
    {
        var inputs = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 0,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        // Captions as wide as they need to be, the field taking the rest, and a third column for
        // the one row that has something to say beside its field.
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        urlBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        memberEmailBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        clientIdBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        memberPasswordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        memberPasswordBox.UseSystemPasswordChar = true;

        maxPages.Anchor = AnchorStyles.Left;
        maxPages.Width = LogicalToDeviceUnits(80);
        maxPages.Minimum = 1;
        maxPages.Maximum = 500;

        localeBox.Anchor = AnchorStyles.Left;
        localeBox.Width = LogicalToDeviceUnits(120);
        localeBox.DropDownStyle = ComboBoxStyle.DropDownList;

        // Items, not DataSource. A bound combo box has no items until its binding context is ready,
        // which is after this constructor has finished, so Restore below could not have selected
        // the remembered locale - it threw, and the window never opened.
        foreach (Locale locale in Enum.GetValues<Locale>())
        {
            localeBox.Items.Add(locale);
        }

        secretStatus.Anchor = AnchorStyles.Left;
        secretStatus.AutoSize = true;

        // Read once, at construction. The variable is fixed for the life of the process, and a
        // label that re-read it would suggest it could be changed without restarting. Left in the
        // ordinary text colour deliberately: report-only is a supported mode, not a fault.
        secretStatus.Text =
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScanOptions.SecretVariable))
                ? $"{ScanOptions.SecretVariable} is not set - write-back will be skipped"
                : $"{ScanOptions.SecretVariable} is set";

        Row("Site URL", urlBox);
        Row("Max pages", maxPages);
        Row("Locale", localeBox);
        Row("Member email", memberEmailBox);
        Row("Member password", memberPasswordBox);
        Row("API client id", clientIdBox, secretStatus);

        dryRun.Text = "Dry run (write nothing)";
        dryRun.AutoSize = true;
        dryRun.Anchor = AnchorStyles.Left;
        dryRun.Margin = new Padding(3, 8, 3, 8);

        run.Text = "Run";
        run.Size = LogicalToDeviceUnits(new Size(110, 32));
        run.Click += OnRunClicked;

        cancel.Text = "Cancel";
        cancel.Size = LogicalToDeviceUnits(new Size(110, 32));
        cancel.Click += OnCancelClicked;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };

        buttons.Controls.Add(run);
        buttons.Controls.Add(cancel);

        log.Dock = DockStyle.Fill;
        log.ReadOnly = true;
        log.BackColor = SystemColors.Window;

        // Fixed-width, and no wrapping: ScanReportWriter.SummaryLines indents its second report
        // path with spaces so it lines up under the first, which only works in a monospaced font
        // and only if a long path is not folded onto the next line.
        log.Font = new Font("Consolas", 9F);
        log.WordWrap = false;

        // Shared with the History tab's detail grid and its diff panel's Appeared/Disappeared
        // grids - see ConfigureCandidateGrid's remarks in MainForm.History.cs. All three grids
        // add rows positionally, so one column list living in one place is what keeps a column
        // added later from silently shifting every grid's data under the wrong headers except
        // the one it was added to.
        ConfigureCandidateGrid(findings);

        findingsSplit.Dock = DockStyle.Fill;
        findingsSplit.Orientation = Orientation.Horizontal;

        // The panel minimums are set in SetInitialSplit, not here - see the remarks there.

        // Every extra pixel of window height goes to the grid, not to both panes. The log
        // auto-scrolls and only its tail is ever read; the grid is a table someone reads in full,
        // and a scan routinely finds ten to forty entries.
        findingsSplit.FixedPanel = FixedPanel.Panel1;
        findingsSplit.Panel1.Controls.Add(log);
        findingsSplit.Panel2.Controls.Add(findings);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 4,
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        root.Controls.Add(inputs, 0, 0);
        root.Controls.Add(dryRun, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        root.Controls.Add(findingsSplit, 0, 3);

        page.Controls.Add(root);

        Restore();

        // The idle state, established by the same method that ends a run, so the two cannot
        // disagree about which controls are live when nothing is happening.
        SetRunning(running: false);

        // SplitterDistance is validated against the container's own height, which is still zero
        // while the constructor runs, so the initial split waits for the first layout pass.
        Load += (_, _) => SetInitialSplit();

        void Row(string caption, Control field, Control? beside = null)
        {
            int row = inputs.RowCount;

            // The caption is the only description this field has, and a Label does not lend it to
            // its neighbour, so it is copied onto the field itself for anything reading the form
            // aloud or by name.
            field.AccessibleName = caption;

            inputs.RowCount = row + 1;
            inputs.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            inputs.Controls.Add(
                new Label
                {
                    Text = caption,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(3, 3, 12, 3),
                },
                0,
                row);

            inputs.Controls.Add(field, 1, row);

            if (beside is null)
            {
                inputs.SetColumnSpan(field, 2);
            }
            else
            {
                inputs.Controls.Add(beside, 2, row);
            }
        }
    }

    /// <summary>Puts the remembered options back into the fields.</summary>
    /// <remarks>
    /// The member password box is left empty, and there is nothing in <see cref="GuiSettings"/> to
    /// fill it from - see that record's remarks. Max pages is clamped rather than trusted: the
    /// settings file is a text file a person can edit, and a value outside the spinner's range
    /// throws when assigned.
    /// </remarks>
    private void Restore()
    {
        urlBox.Text = settings.Url;
        maxPages.Value = Math.Clamp(settings.MaxPages, (int)maxPages.Minimum, (int)maxPages.Maximum);
        localeBox.SelectedIndex = Math.Max(0, localeBox.Items.IndexOf(settings.Locale));
        memberEmailBox.Text = settings.MemberEmail;
        clientIdBox.Text = settings.ClientId;
        dryRun.Checked = settings.DryRun;
    }

    /// <summary>Sizes the log and findings panes, once the container has a real height.</summary>
    /// <remarks>
    /// Both minimums and the splitter position are applied here rather than in the constructor, and
    /// in that order, because every one of them is validated against the container's current height
    /// - which is the default 100 until the form has laid out. Assigning a DPI-scaled 180-pixel
    /// minimum to a 100-pixel container throws out of the constructor, and a window that throws in
    /// its constructor is a window that never opens.
    /// </remarks>
    private void SetInitialSplit()
    {
        int minimum = LogicalToDeviceUnits(120);

        if (findingsSplit.Height < (minimum * 2) + findingsSplit.SplitterWidth)
        {
            return;
        }

        findingsSplit.Panel1MinSize = minimum;
        findingsSplit.Panel2MinSize = minimum;

        // Two fifths to the log, three to the grid - see FixedPanel for why the grid is the pane
        // worth the space. Measured, so this ratio needs no DPI scaling of its own.
        findingsSplit.SplitterDistance = Math.Clamp(
            findingsSplit.Height * 2 / 5,
            minimum,
            findingsSplit.Height - minimum - findingsSplit.SplitterWidth);
    }

    private async void OnRunClicked(object? sender, EventArgs e)
    {
        ScanOptions options;

        try
        {
            options = BuildOptions();
        }
        catch (ArgumentException error)
        {
            MessageBox.Show(this, error.Message, "Cannot start", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }

        // Remembered as soon as the options are known good, rather than only when the scan works.
        // A scan that fails is exactly when the operator has typed something worth not losing - a
        // URL that turned out to resolve to nothing, a client id being tried for the first time -
        // and that was the run that used to discard it.
        Remembered().Save();

        SetRunning(true);
        log.Clear();
        findings.Rows.Clear();
        cancellation = new CancellationTokenSource();

        var scanLog = new TextBoxScanLog(log);

        // Copied out of the field: the field is nullable and reassigned per run, and the lambda
        // below outlives the statement that assigned it.
        CancellationToken token = cancellation.Token;

        try
        {
            // Task.Run so Playwright's synchronous startup cannot block the UI thread.
            ScanResult? result = await Task.Run(
                () => new ScanRunner(options, () => CatalogueSource.Load(scanLog), scanLog)
                    .RunAsync(token),
                token);

            if (result is null)
            {
                scanLog.Warning("The scan found no pages, so there is nothing to report.");

                return;
            }

            // Shown before anything is written. A report file left open in an editor, or a full
            // disk, used to throw straight past this and cost the operator the findings of a scan
            // that had actually succeeded.
            ShowResult(result);

            (string? reportFailure, string? historyFailure) = TryWrite(options, result);

            if (historyFailure is null)
            {
                // TryWrite is what added the new entry - see its own remarks - so only a
                // successful history write has anything new for the History tab to show,
                // independent of whether the report itself was written. Refreshing here, not
                // only on tab activation, is what makes a run that finishes while the operator
                // is already looking at that tab show up without switching away and back.
                RefreshHistoryList();
            }

            foreach (string line in ScanReportWriter.SummaryLines(options, result))
            {
                scanLog.Info(line);
            }

            // After the summary, so a red line is the last thing left on screen rather than the
            // summary's "Report written to ..." - which those two lines say either way, because
            // they are the console tool's text and not this window's to reword.
            if (reportFailure is not null)
            {
                scanLog.Warning($"The scan finished, but its report could not be written: {reportFailure}");
            }

            if (historyFailure is not null)
            {
                scanLog.Warning(
                    $"The scan finished, but its history entry could not be written: {historyFailure}");
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled scan writes no report and produces no result: a partial scan presented
            // as a complete one would be worse than no scan at all.
            scanLog.Warning("Cancelled. No report was written.");
        }
        catch (Exception error)
        {
            scanLog.Warning($"The scan failed: {error.Message}");
        }
        finally
        {
            SetRunning(false);

            // Disposed here and nowhere else: SetRunning has just disabled Cancel, and everything
            // in this block runs on the UI thread, so no click can interleave and reach a disposed
            // source. Nulled so the next run cannot be handed the spent one.
            cancellation.Dispose();
            cancellation = null;
        }
    }

    /// <remarks>
    /// Says so in the log, because the engine only observes a cancel between passes: without a
    /// line here the window looks like it ignored the click for the rest of the current pass.
    /// </remarks>
    private void OnCancelClicked(object? sender, EventArgs e)
    {
        cancel.Enabled = false;

        new TextBoxScanLog(log).Info("Cancelling - the scan stops at the end of the pass it is running.");

        cancellation?.Cancel();
    }

    /// <summary>
    /// Turns the fields into the same <see cref="ScanOptions"/> the command line would have built.
    /// </summary>
    /// <remarks>
    /// The URL is checked with <see cref="Uri.TryCreate"/> against <see cref="UriKind.Absolute"/>,
    /// which is the rule <see cref="ScanOptions.Parse"/> applies, and the message names the same
    /// likely cause - a URL pasted without its scheme. Only the wording differs, because a window
    /// telling someone to fix a flag it does not have would be nonsense. Two front ends accepting
    /// different URLs is a bug nobody could reproduce from the other one.
    /// </remarks>
    /// <exception cref="ArgumentException">The site URL is not absolute.</exception>
    private ScanOptions BuildOptions()
    {
        string url = urlBox.Text.Trim();

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? root) is false)
        {
            throw new ArgumentException(
                $"'{url}' is not an absolute URL. It needs a scheme, for example https://ndstk.se");
        }

        return new ScanOptions(
            Url: root,
            // The policy page lives on the site being scanned. --target exists for the case where
            // it does not, which is a console-tool concern: nothing in the window offers it, so
            // the root here is the deliberate answer rather than an omission.
            Target: root,
            MaxPages: (int)maxPages.Value,
            Locale: SelectedLocale,
            MemberEmail: Supplied(memberEmailBox.Text),
            MemberPassword: Supplied(memberPasswordBox.Text),
            ClientId: Supplied(clientIdBox.Text),
            // From the environment, never from a field: the same reason the console tool refuses a
            // --client-secret flag.
            ClientSecret: Environment.GetEnvironmentVariable(ScanOptions.SecretVariable),
            DryRun: dryRun.Checked,
            ReportDir: ReportDirectory,
            // Headless, always. --headed exists to debug the engine; a window that opened a second
            // visible browser on every run would be answering a question nobody asked.
            Headed: false);

        // Blank means absent, matching the console tool, where a flag that was not passed is null.
        static string? Supplied(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>The fields as the six things worth keeping until next time.</summary>
    /// <remarks>
    /// Separate from <see cref="BuildOptions"/> so that method stays a pure read of the form rather
    /// than a read with a side effect on a field. The client secret and the member password have no
    /// member here to be captured into, deliberately - see <see cref="GuiSettings"/>.
    /// </remarks>
    private GuiSettings Remembered() => new(
        Url: urlBox.Text.Trim(),
        MaxPages: (int)maxPages.Value,
        Locale: SelectedLocale,
        MemberEmail: memberEmailBox.Text.Trim(),
        ClientId: clientIdBox.Text.Trim(),
        DryRun: dryRun.Checked);

    /// <summary>
    /// Writes the report files and the history entry, each independently. Returns the failure
    /// message for each, or null where that write succeeded.
    /// </summary>
    /// <remarks>
    /// Failing to write is not the same as failing to scan, and the caller has already put the
    /// findings on screen by the time this runs. The two writes are guarded separately and
    /// neither is allowed to stop the other: history is written "in addition to" the report
    /// directory, not after it, so a full report disk must not cost the scan its place in
    /// history, and a locked-down history folder must not cost the report. Narrow on purpose,
    /// unlike the settings write: these files are the point of the exercise, so anything other
    /// than the disk refusing them should still reach the caller's own handler.
    /// </remarks>
    private static (string? ReportFailure, string? HistoryFailure) TryWrite(ScanOptions options, ScanResult result)
    {
        string? reportFailure = null;
        string? historyFailure = null;

        try
        {
            ScanReportWriter.WriteFiles(options, result);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            reportFailure = error.Message;
        }

        try
        {
            ScanHistory.Save(result);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            historyFailure = error.Message;
        }

        return (reportFailure, historyFailure);
    }

    /// <remarks>
    /// Re-enabling happens in the caller's finally block, so a scan that throws cannot leave the
    /// window permanently dead. That same finally block is why the disposed check is here: closing
    /// the window mid-scan is the one way this runs with nothing left to re-enable.
    /// </remarks>
    private void SetRunning(bool running)
    {
        if (IsDisposed)
        {
            return;
        }

        foreach (Control input in Inputs)
        {
            input.Enabled = running is false;
        }

        cancel.Enabled = running;
        Cursor = running ? Cursors.AppStarting : Cursors.Default;
    }

    private void ShowResult(ScanResult result) => FillFindings(findings, result);

    /// <summary>
    /// Fills a findings-style grid from one scan's result: the Scan tab's own grid live, and the
    /// History tab's single-scan detail view once a past scan is reloaded. The one place the
    /// violation-matching rule and the row-per-candidate projection exist, so the two grids read
    /// a completed scan identically rather than through two copies that could drift apart.
    /// </summary>
    /// <remarks>
    /// Candidates and Violations are computed from different inputs, on purpose: Candidates is
    /// the earliest sighting per name, while Violations is scanned over the raw observations,
    /// because a violation belongs to one sighting rather than to a name. So a cookie first seen
    /// in a pass that granted it and set again in a pass that did not carries Flag = None here
    /// while still being in Violations - driving ExitCode 1 and the log's CONSENT VIOLATION(S)
    /// line. A grid that coloured only by Flag would show that row as ordinary white and
    /// contradict the exit code CI gates on, which is the divergence ScanRunner exists to
    /// prevent. Both lists come out of the same Classify call, so the names match; compared
    /// ordinal-insensitively because cookie names are not the place to be fussy about case.
    /// </remarks>
    private static void FillFindings(DataGridView grid, ScanResult result)
    {
        HashSet<string> violations = new(
            result.Violations.Select(violation => violation.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (CookieDeclarationCandidate candidate in result.Candidates)
        {
            int index = grid.Rows.Add(
                candidate.Name,
                candidate.StorageType,
                candidate.Category,
                candidate.FirstSeenPass.ToString(),
                candidate.Duration);

            Colour(grid.Rows[index], candidate.Flag, violations.Contains(candidate.Name));
        }
    }

    /// <summary>Colours the rows that need a human, and leaves the rest alone.</summary>
    /// <remarks>
    /// The selection colours are set to match, so clicking a row does not hide the one thing the
    /// colour was there to say.
    /// </remarks>
    private static void Colour(DataGridViewRow row, CandidateFlag flag, bool violation)
    {
        if (flag == CandidateFlag.Violation || violation)
        {
            row.DefaultCellStyle.BackColor = Color.Firebrick;
            row.DefaultCellStyle.SelectionBackColor = Color.Firebrick;

            // Firebrick is dark enough that the default near-black text on it is unreadable.
            row.DefaultCellStyle.ForeColor = Color.White;
            row.DefaultCellStyle.SelectionForeColor = Color.White;

            return;
        }

        if (flag == CandidateFlag.NeedsReview)
        {
            row.DefaultCellStyle.BackColor = Color.DarkOrange;
            row.DefaultCellStyle.SelectionBackColor = Color.DarkOrange;
        }
    }
}
