namespace NDSTK.CookieScanner.Gui;

/// <summary>
/// The scanner's window: a Scan tab that runs the engine, and a History tab that reads back what
/// past runs found.
/// </summary>
/// <remarks>
/// Built in code rather than with the designer. A generated designer file is awkward to review in
/// a diff, and this window is two tabs of stock controls.
/// <para>
/// Partial across one file per tab - <c>MainForm.Scan.cs</c> and <c>MainForm.History.cs</c> - so
/// the shell stays readable as the tabs grow. Each tab builds its own controls; this file only
/// decides the window and the order of the tabs.
/// </para>
/// </remarks>
public sealed partial class MainForm : Form
{
    private readonly TabControl tabs = new() { Name = "tabs", Dock = DockStyle.Fill };
    private readonly TabPage scanTab = new("Scan") { Name = "scanTab" };
    private readonly TabPage historyTab = new("History") { Name = "historyTab" };

    public MainForm()
    {
        Name = "mainForm";
        Text = "NDSTK cookie scanner";

        // Every size on this form is a logical unit put through LogicalToDeviceUnits, never a raw
        // device pixel. The form is PerMonitorV2 aware and does not auto-scale what the constructor
        // assigns, so an unscaled 1000x700 rendered as an effective 667x467 at 150% - and a bigger
        // constant would only have moved the problem to a 100% display.
        ClientSize = LogicalToDeviceUnits(new Size(1000, 700));

        // A floor rather than a fixed size: the findings grid has five columns and the log pane
        // holds full URLs, and both stop being readable well before the window stops resizing.
        MinimumSize = LogicalToDeviceUnits(new Size(900, 620));
        StartPosition = FormStartPosition.CenterScreen;

        BuildScanTab(scanTab);

        tabs.TabPages.Add(scanTab);
        tabs.TabPages.Add(historyTab);

        Controls.Add(tabs);
    }
}
