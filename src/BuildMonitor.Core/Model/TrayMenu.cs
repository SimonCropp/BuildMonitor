/// <summary>
/// The ids of the tray menu items, and the glyphs they carry.
/// </summary>
static class TrayMenu
{
    public const string Open = "open";
    public const string Refresh = "refresh";
    public const string Connections = "connections";
    public const string Options = "options";
    public const string Filters = "filters";
    public const string CodeDirectory = "codeDirectory";
    public const string Logs = "logs";
    public const string Issue = "issue";
    public const string Update = "update";
    public const string Exit = "exit";

    /// <summary>
    /// The glyph of every item the menu can hold, which the tests hold to a picture each. Every
    /// platform's tray asks for a glyph by name as the menu first names it, so one missing from the
    /// pictures is drawn as no glyph rather than failing.
    /// </summary>
    public static readonly IReadOnlyList<string> Glyphs = ["open", "refresh", "connections", "options", "filters", "folder", "logs", "issue", "update", "exit"];
}
