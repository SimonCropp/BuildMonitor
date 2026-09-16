/// <summary>
/// The ids of the tray menu items, and the glyphs they carry.
/// </summary>
static class TrayMenu
{
    public const string Open = "open";
    public const string Refresh = "refresh";
    public const string Options = "options";
    public const string Filters = "filters";
    public const string CodeDirectory = "codeDirectory";
    public const string Logs = "logs";
    public const string Issue = "issue";
    public const string Update = "update";
    public const string Exit = "exit";

    /// <summary>
    /// The glyph of every item the menu can hold. The macOS head is handed these at start and draws
    /// no other, so a list of its own left Open code directory without its folder there, and went
    /// on loading the glyphs of the per build items after those left the menu.
    /// </summary>
    public static readonly IReadOnlyList<string> Glyphs = ["open", "refresh", "options", "filters", "folder", "logs", "issue", "update", "exit"];
}
