using System.Globalization;
using NDSTK.CookieScan.Core;
using NDSTK.CookieScanner;

namespace NDSTK.CookieScanner.Gui;

/// <summary>
/// The History tab: every past scan, kept by <see cref="ScanHistory"/> so the command line and
/// this window share one record, one selected in detail and two compared.
/// </summary>
public sealed partial class MainForm
{
    private readonly ScanHistory history = ScanHistory.Default();

    private readonly SplitContainer historySplit = new() { Name = "historySplit" };
    private readonly ListView historyList = new() { Name = "historyList", AccessibleName = "Scan history" };
    private readonly Button compareButton = new() { Name = "compareButton" };
    private readonly Label compareStatus = new() { Name = "compareStatus" };

    // The plain detail view - one scan's findings, same shape as the Scan tab's grid - and the
    // diff view are two alternative contents for the right pane. Both are built up front and
    // toggled with Visible rather than swapped in and out of Controls, so switching between them
    // is not itself a place layout could go wrong.
    private readonly DataGridView historyGrid =
        new() { Name = "historyGrid", AccessibleName = "Selected scan" };

    private readonly Panel diffPanel = new() { Name = "diffPanel" };
    private readonly Label diffHeader = new() { Name = "diffHeader" };
    private readonly TableLayoutPanel diffGroups = new() { Name = "diffGroups" };
    private readonly Label diffEmpty = new() { Name = "diffEmpty" };
    private readonly GroupBox appearedGroup = new() { Name = "appearedGroup" };
    private readonly GroupBox disappearedGroup = new() { Name = "disappearedGroup" };
    private readonly GroupBox recategorisedGroup = new() { Name = "recategorisedGroup" };
    private readonly DataGridView appearedGrid = new() { Name = "appearedGrid" };
    private readonly DataGridView disappearedGrid = new() { Name = "disappearedGrid" };
    private readonly DataGridView recategorisedGrid = new() { Name = "recategorisedGrid" };

    // Guards SetInitialHistorySplit so a later call - the tab can be activated more than once -
    // never overwrites a split the operator has since dragged by hand. See that method's remarks
    // for why it can be called more than once in the first place.
    private bool historySplitReady;

    private void BuildHistoryTab(TabPage page)
    {
        historyList.View = View.Details;
        historyList.FullRowSelect = true;
        historyList.Dock = DockStyle.Fill;

        // True is the default already; set explicitly so it reads as a decision - Compare needs
        // exactly two, which is not choosable from a single-select list.
        historyList.MultiSelect = true;

        historyList.Columns.Add("Completed", LogicalToDeviceUnits(150));
        historyList.Columns.Add("Site", LogicalToDeviceUnits(200));
        historyList.Columns.Add("Entries", LogicalToDeviceUnits(70));
        historyList.Columns.Add("Exit code", LogicalToDeviceUnits(80));
        historyList.SelectedIndexChanged += OnHistorySelectionChanged;

        compareButton.Text = "Compare";
        compareButton.Enabled = false;
        compareButton.Size = LogicalToDeviceUnits(new Size(110, 32));
        compareButton.Click += OnCompareClicked;

        compareStatus.AutoSize = true;
        compareStatus.Margin = new Padding(8, 10, 3, 3);
        compareStatus.Text = "Select two scans to compare";

        var compareRow = new FlowLayoutPanel
        {
            Name = "compareRow",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0),
        };

        compareRow.Controls.Add(compareButton);
        compareRow.Controls.Add(compareStatus);

        var left = new TableLayoutPanel
        {
            Name = "left",
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
        };

        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(historyList, 0, 0);
        left.Controls.Add(compareRow, 0, 1);

        ConfigureCandidateGrid(historyGrid);

        BuildDiffPanel();

        historySplit.Dock = DockStyle.Fill;
        historySplit.Orientation = Orientation.Vertical;

        // As in the Scan tab's findingsSplit: the panel minimums and the initial distance are set
        // in SetInitialHistorySplit, not here, because both are validated against the container's
        // current width - zero until the form has laid out - and a fixed panel here would only
        // move that same crash into this constructor instead of preventing it.
        historySplit.FixedPanel = FixedPanel.Panel1;
        historySplit.Panel1.Controls.Add(left);
        historySplit.Panel2.Controls.Add(historyGrid);
        historySplit.Panel2.Controls.Add(diffPanel);

        page.Padding = new Padding(8);
        page.Controls.Add(historySplit);

        ShowDetailPane();

        Load += (_, _) => SetInitialHistorySplit();

        // tabs is declared in MainForm.cs; reached here because both files are the one partial
        // class. Refreshing on activation - not only once at startup - is what makes a scan run
        // from the console, or from this window a minute ago, show up without restarting the
        // window; refreshing after a scan completes is the other half, wired from OnRunClicked in
        // MainForm.Scan.cs.
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab == page)
            {
                OnHistoryTabActivated();
            }
        };

        // So the tab already has content the first time it is opened, rather than only from the
        // second visit onward.
        RefreshHistoryList();
    }

    private void BuildDiffPanel()
    {
        ConfigureCandidateGrid(appearedGrid);
        ConfigureCandidateGrid(disappearedGrid);

        recategorisedGrid.Dock = DockStyle.Fill;
        recategorisedGrid.ReadOnly = true;
        recategorisedGrid.AllowUserToAddRows = false;
        recategorisedGrid.AllowUserToDeleteRows = false;
        recategorisedGrid.RowHeadersVisible = false;
        recategorisedGrid.MultiSelect = false;
        recategorisedGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        recategorisedGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        recategorisedGrid.BackgroundColor = SystemColors.Window;
        recategorisedGrid.Columns.Add("name", "Name");
        recategorisedGrid.Columns.Add("from", "From");
        recategorisedGrid.Columns.Add("to", "To");

        appearedGroup.Text = "Appeared";
        appearedGroup.Dock = DockStyle.Fill;
        appearedGroup.Padding = new Padding(6);
        appearedGroup.Controls.Add(appearedGrid);

        disappearedGroup.Text = "Disappeared";
        disappearedGroup.Dock = DockStyle.Fill;
        disappearedGroup.Padding = new Padding(6);
        disappearedGroup.Controls.Add(disappearedGrid);

        recategorisedGroup.Text = "Recategorised";
        recategorisedGroup.Dock = DockStyle.Fill;
        recategorisedGroup.Padding = new Padding(6);
        recategorisedGroup.Controls.Add(recategorisedGrid);

        diffGroups.Dock = DockStyle.Fill;
        diffGroups.ColumnCount = 3;
        diffGroups.RowCount = 1;
        diffGroups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        diffGroups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        diffGroups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        diffGroups.Controls.Add(appearedGroup, 0, 0);
        diffGroups.Controls.Add(disappearedGroup, 1, 0);
        diffGroups.Controls.Add(recategorisedGroup, 2, 0);

        diffEmpty.Text = "Nothing changed between these two scans";
        diffEmpty.AutoSize = true;
        diffEmpty.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        // diffBody (below) is a plain Panel, not a TableLayoutPanel or FlowLayoutPanel - only
        // those two layout engines honour Margin, so a Margin here would be silently inert and
        // the label would render flush against the panel's top-left corner. Location is what
        // actually moves an Anchor-positioned control in a plain Panel.
        diffEmpty.Location = new Point(3, 12);

        // Both live in the same cell, Dock-filled on top of one another; ShowDiff toggles which
        // one is Visible rather than adding and removing controls, for the same reason the detail
        // and diff panes themselves are toggled that way.
        var diffBody = new Panel { Name = "diffBody", Dock = DockStyle.Fill };
        diffBody.Controls.Add(diffGroups);
        diffBody.Controls.Add(diffEmpty);

        diffHeader.AutoSize = true;
        diffHeader.Anchor = AnchorStyles.Left;
        diffHeader.Margin = new Padding(3, 3, 3, 8);

        var diffLayout = new TableLayoutPanel
        {
            Name = "diffLayout",
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
        };
        diffLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        diffLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        diffLayout.Controls.Add(diffHeader, 0, 0);
        diffLayout.Controls.Add(diffBody, 0, 1);

        diffPanel.Dock = DockStyle.Fill;
        diffPanel.Controls.Add(diffLayout);
    }

    /// <summary>
    /// The column layout and grid behaviour the Scan tab's findings grid, the History tab's
    /// detail grid, and the diff panel's Appeared/Disappeared grids all share.
    /// </summary>
    /// <remarks>
    /// Columns and grid behaviour only - not the colouring rule. That rule lives in one place,
    /// <see cref="Colour"/> in <c>MainForm.Scan.cs</c>, and callers here reach it directly rather
    /// than through a second copy, because a partial class's private members are visible from
    /// every file it is split across. Two implementations of "which rows turn red" is exactly the
    /// defect that was just fixed on the Scan tab; the way to not reintroduce it is to have only
    /// one such implementation to begin with.
    /// </remarks>
    private static void ConfigureCandidateGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // The empty area below the rows, which defaults to a flat grey slab - most of what a grid
        // shows before it has anything in it.
        grid.BackgroundColor = SystemColors.Window;

        int nameColumn = grid.Columns.Add("name", "Name");

        grid.Columns.Add("storage", "Storage");
        grid.Columns.Add("category", "Category");
        grid.Columns.Add("firstSeen", "First seen in");
        grid.Columns.Add("duration", "Duration");

        // Names are the longest values in the grid by a wide margin - the member cookie alone is
        // thirty characters, which does not fit an even fifth of the width. Addressed by the index
        // Add returned, because the by-name indexer is nullable and this one is not.
        grid.Columns[nameColumn].FillWeight = 200F;
    }

    /// <summary>Sizes the list and detail panes, once the container has a real width.</summary>
    /// <remarks>
    /// Deferred for the same reason <c>SetInitialSplit</c> is on the Scan tab: Panel1MinSize,
    /// Panel2MinSize and SplitterDistance are all validated against the container's current width
    /// - zero at construction time - and assigning any of them too early throws out of the
    /// constructor, which is a window that never opens.
    /// <para>
    /// Called from both the form's Load and the tab's own activation, because a TabPage that is
    /// not the one selected when the form first loads is not guaranteed to have been laid out by
    /// then. <see cref="historySplitReady"/> makes the second call, whichever one it turns out to
    /// be, a no-op rather than a reset of a split the operator may have since dragged.
    /// </para>
    /// </remarks>
    private void SetInitialHistorySplit()
    {
        if (historySplitReady)
        {
            return;
        }

        // Derived from the columns themselves, not a guessed constant: a hardcoded minimum here
        // drifted out of step with the four column widths above it and left "Exit code" - the one
        // column an operator scans this list for - clipped off at the window's default size. The
        // scrollbar and border allowances are why a list sized to exactly the column total still
        // clipped the last column once there were enough rows to need a scrollbar.
        int listMinimum = historyList.Columns.Cast<ColumnHeader>().Sum(column => column.Width)
            + SystemInformation.VerticalScrollBarWidth
            + (SystemInformation.Border3DSize.Width * 2);

        int detailMinimum = LogicalToDeviceUnits(360);

        if (historySplit.Width < listMinimum + detailMinimum + historySplit.SplitterWidth)
        {
            return;
        }

        historySplit.Panel1MinSize = listMinimum;
        historySplit.Panel2MinSize = detailMinimum;

        // Two fifths to the list, three to the detail pane - the list is short lines of text, the
        // detail pane is a grid that is read in full and, in the diff view, three of them side by
        // side.
        historySplit.SplitterDistance = Math.Clamp(
            historySplit.Width * 2 / 5,
            listMinimum,
            historySplit.Width - detailMinimum - historySplit.SplitterWidth);

        historySplitReady = true;
    }

    private void OnHistoryTabActivated()
    {
        RefreshHistoryList();
        SetInitialHistorySplit();
    }

    /// <summary>Reloads the list from disk, newest first, as <see cref="ScanHistory.List"/> gives it.</summary>
    private void RefreshHistoryList()
    {
        historyList.BeginUpdate();

        try
        {
            historyList.Items.Clear();

            foreach (ScanHistoryEntry entry in history.List())
            {
                var item = new ListViewItem(FormatTime(entry.CompletedAt)) { Tag = entry };

                item.SubItems.Add(entry.Site);
                item.SubItems.Add(entry.EntryCount.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(entry.ExitCode.ToString(CultureInfo.InvariantCulture));

                historyList.Items.Add(item);
            }
        }
        finally
        {
            historyList.EndUpdate();
        }

        // Items.Clear() drops the selection, which raises SelectedIndexChanged on its own and
        // puts the compare button and the detail pane back to the no-selection state - nothing
        // further to do here for that.
    }

    private IReadOnlyList<ScanHistoryEntry> SelectedEntries() =>
        [.. historyList.SelectedItems.Cast<ListViewItem>().Select(item => (ScanHistoryEntry)item.Tag!)];

    private void OnHistorySelectionChanged(object? sender, EventArgs e)
    {
        IReadOnlyList<ScanHistoryEntry> selected = SelectedEntries();

        compareButton.Enabled = selected.Count == 2;
        compareStatus.Text = selected.Count == 2 ? string.Empty : "Select two scans to compare";

        // A changed selection invalidates whatever the right pane was showing - most of all a diff
        // for a pair that may no longer be the one selected - so the plain detail view always
        // comes back first; ShowSingle below fills it back in for the one case that has content.
        ShowDetailPane();
        historyGrid.Rows.Clear();

        if (selected is [ScanHistoryEntry only])
        {
            ShowSingle(only);
        }
    }

    private void ShowSingle(ScanHistoryEntry entry)
    {
        ScanResult? result = history.Load(entry);

        if (result is null)
        {
            MessageBox.Show(this, "That scan's report file could not be read.", "History",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }

        // Same method as the Scan tab's ShowResult - see FillFindings in MainForm.Scan.cs, which
        // holds the one copy of both the violation-matching rule and the row projection. Both
        // Candidates and Violations come from this one loaded ScanResult, so this reproduces
        // exactly what that scan's own run showed on screen, not a re-derived approximation of it.
        FillFindings(historyGrid, result);
    }

    private void OnCompareClicked(object? sender, EventArgs e)
    {
        if (SelectedEntries() is not [ScanHistoryEntry first, ScanHistoryEntry second])
        {
            return;
        }

        // Ordered by time, not by click order, so "appeared" always means "appeared in the newer
        // one" no matter which row was selected first.
        (ScanHistoryEntry older, ScanHistoryEntry newer) =
            first.CompletedAt <= second.CompletedAt ? (first, second) : (second, first);

        ScanResult? before = history.Load(older);
        ScanResult? after = history.Load(newer);

        if (before is null || after is null)
        {
            MessageBox.Show(this, "One of those scans could not be read.", "Compare",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }

        ShowDiff(ScanDiff.Between(before.Candidates, after.Candidates), older, newer);
    }

    /// <summary>
    /// Fills the right pane with what changed between two scans already put in time order by
    /// <see cref="OnCompareClicked"/> - three labelled groups, or one line when none of the three
    /// lists have anything in them.
    /// </summary>
    private void ShowDiff(ScanDiff diff, ScanHistoryEntry older, ScanHistoryEntry newer)
    {
        diffHeader.Text =
            $"Comparing {FormatTime(older.CompletedAt)} to {FormatTime(newer.CompletedAt)}";

        bool nothingChanged = diff.Appeared.Count == 0
            && diff.Disappeared.Count == 0
            && diff.Recategorised.Count == 0;

        diffGroups.Visible = nothingChanged is false;
        diffEmpty.Visible = nothingChanged;

        if (nothingChanged is false)
        {
            appearedGroup.Text = $"Appeared ({diff.Appeared.Count})";
            disappearedGroup.Text = $"Disappeared ({diff.Disappeared.Count})";
            recategorisedGroup.Text = $"Recategorised ({diff.Recategorised.Count})";

            appearedGrid.Rows.Clear();
            disappearedGrid.Rows.Clear();
            recategorisedGrid.Rows.Clear();

            FillCandidateGrid(appearedGrid, diff.Appeared);
            FillCandidateGrid(disappearedGrid, diff.Disappeared);

            foreach (CategoryChange change in diff.Recategorised)
            {
                recategorisedGrid.Rows.Add(change.Name, change.From, change.To);
            }
        }

        ShowDiffPane();
    }

    /// <summary>
    /// Fills one of the diff panel's Appeared/Disappeared grids. Not coloured:
    /// <see cref="ShowDiff"/> only has the <see cref="ScanDiff"/> it was given - candidates, not
    /// the raw observations <see cref="ScanResult.Violations"/> is scanned from - so colouring
    /// here would mean either wiring the two loaded <see cref="ScanResult"/>s that
    /// <see cref="OnCompareClicked"/> holds through as well, or colouring by
    /// <see cref="CookieDeclarationCandidate.Flag"/> alone, which is exactly the half of the rule
    /// that <see cref="Colour"/>'s remarks warn is not enough on its own. Showing a partial rule
    /// would be worse than showing none; the fuller version is a real option, just not one this
    /// pass took.
    /// </summary>
    private static void FillCandidateGrid(
        DataGridView grid, IEnumerable<CookieDeclarationCandidate> candidates)
    {
        foreach (CookieDeclarationCandidate candidate in candidates)
        {
            grid.Rows.Add(
                candidate.Name,
                candidate.StorageType,
                candidate.Category,
                candidate.FirstSeenPass.ToString(),
                candidate.Duration);
        }
    }

    private void ShowDetailPane()
    {
        diffPanel.Visible = false;
        historyGrid.Visible = true;
    }

    private void ShowDiffPane()
    {
        historyGrid.Visible = false;
        diffPanel.Visible = true;
    }

    private static string FormatTime(DateTimeOffset time) =>
        time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
