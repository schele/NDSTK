using System.Text.Json;
using System.Text.Json.Serialization;
using NDSTK.CookieScan.Core;

namespace NDSTK.CookieScanner.Gui;

/// <summary>
/// What the window remembers between runs.
/// </summary>
/// <remarks>
/// The client secret and the member password are deliberately absent and must stay absent. The
/// console tool refuses a --client-secret flag so a secret cannot reach shell history; a settings
/// file storing one would undo that to save a paste. The secret comes from
/// NDSTK_COOKIESCAN_CLIENT_SECRET and the member password is typed per run.
/// </remarks>
public sealed record GuiSettings(
    string Url = "https://localhost:44351",
    int MaxPages = 25,
    Locale Locale = Locale.Sv,
    string MemberEmail = "",
    string ClientId = "",
    bool DryRun = true)
{
    // Save and Load must share one instance: the same latent bug the team deliberately fixed in
    // ScanJson otherwise reappears here - Locale would serialise as an integer, and reordering the
    // enum would silently change a saved setting's meaning. JsonStringEnumConverter reads numbers
    // back as well as names by default (only allowIntegerValues: false would turn that off, and
    // this does not set it), so an existing settings file holding "Locale": 0 still loads.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NDSTK.CookieScanner",
        "settings.json");

    public static GuiSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(Path), Options) ?? new GuiSettings()
                : new GuiSettings();
        }
        catch (Exception)
        {
            // Unreadable settings are not worth refusing to start over, and the catch is broad
            // deliberately: this runs from a field initializer, on the constructor path, where
            // anything that escapes means no window at all rather than a window with defaults in
            // it. A remembered URL is a convenience; nothing about reading one earns that risk.
            return new GuiSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception)
        {
            // Losing the remembered settings is a nuisance, not a reason to fail a scan. Broad for
            // the same reason as Load, and for one more: the caller invokes this outside its own
            // try, so anything that escapes here would reach the message loop unhandled.
        }
    }
}
