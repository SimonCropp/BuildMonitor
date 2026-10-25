/// <summary>
/// The Linux tray: PlatformHeads' StatusNotifierItem, with this app's names and pictures, and the
/// attention icon marked as NeedsAttention for a host that draws that differently.
/// </summary>
static class LinuxTray
{
    public static ITray? Open(out string? error) =>
        StatusNotifierTray<TrayIconKind>.Open(
            new(
                "BuildMonitor",
                ScreenBuilder.Title,
                Images.TrayArgb,
                Images.Glyph,
                _ => _ == TrayIconKind.Attention,
                Failed),
            out error);

    static void Failed(Exception exception, string what) =>
        Log.Warning(exception, "Tray: {What}", what);
}
