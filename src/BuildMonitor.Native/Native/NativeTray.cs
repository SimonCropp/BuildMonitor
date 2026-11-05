/// <summary>
/// The macOS menu bar item: PlatformHeads.Mac's <see cref="StatusItemTray{TIcon}" />, whose ABI the
/// native library exports beside bm.h. Its events are AppKit's, which <c>bm_present</c> pumps for
/// the window as well, asking the tray whether a click is waiting so it can end its wait. The
/// application around it is the window's: <c>bm_tray_init</c> makes it an accessory until a window
/// is shown, and answers its quits.
/// </summary>
static class NativeTray
{
    public static ITray? Open(out string? error)
    {
        try
        {
            if (Bm.TrayAvailable() != 1)
            {
                error = "The native renderer has no tray on this platform.";
                return null;
            }

            if (Bm.TrayInit() != 1)
            {
                error = "The native renderer could not make the application a tray app's.";
                return null;
            }
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            error = exception.Message;
            return null;
        }

        // 48 pixels, which is two and a bit times the 18 point menu bar height AppKit scales it to.
        return StatusItemTray<TrayIconKind>.Open(
            new(
                Bm.Library,
                "BuildMonitor",
                (kind, _) => Images.TrayPng(kind, 48),
                (name, _) => Images.Glyph(name, 32)),
            out error);
    }
}
