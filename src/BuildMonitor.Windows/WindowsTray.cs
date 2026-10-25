/// <summary>
/// The Windows tray: PlatformHeads' NotifyIcon, with this app's pictures, and its menus in this
/// app's palette rather than the system's.
/// </summary>
static class WindowsTray
{
    public static ITray? Open(out string? error) =>
        NotifyIconTray<TrayIconKind>.Open(
            new(ScreenBuilder.Title, Icons.Tray, _ => Icons.Glyph(_), MenuTheme.Apply, Style),
            out error);

    static void Style(ToolStripMenuItem menuItem, TrayMenuItem item) =>
        menuItem.ForeColor = item.Enabled ? Palette.Text : Palette.Dim;
}
