/// <summary>
/// The macOS menu bar item, drawn by the native library. The model rides in every
/// <c>bm_present</c> and the clicks come back through <c>bm_poll_input</c>, so this only hands
/// over the icons at start and says whether a tray exists at all.
/// </summary>
sealed class NativeTray : ITray
{
    NativeTray()
    {
    }

    public static ITray? Open(out string? error)
    {
        error = null;
        try
        {
            if (Bm.TrayAvailable() != 1)
            {
                error = "The native renderer has no tray on this platform.";
                return null;
            }

            if (Bm.TrayInit() != 1)
            {
                error = "The native renderer could not create a tray item.";
                return null;
            }
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            error = exception.Message;
            return null;
        }

        foreach (var kind in Enum.GetValues<TrayIconKind>())
        {
            // 48 pixels, which is two and a bit times the 18 point menu bar height AppKit scales it to.
            var bytes = Images.TrayPng(kind, 48);
            if (bytes is null)
            {
                continue;
            }

            fixed (byte* pointer = bytes)
            {
                // The IDE reports this as redundant; the compiler's CS9363 requires it.
                // ReSharper disable once RedundantUnsafeContext
                unsafe
                {
                    Bm.TraySetIcon((int) kind, pointer, bytes.Length);
                }
            }
        }

        string[] glyphs = ["open", "refresh", "options", "filters", "logs", "issue", "update", "exit", "build", "retry", "cancel", "queued", "running", "succeeded", "failed", "cancelled", "unknown"];
        foreach (var name in glyphs.Concat(ProviderDescriptors.All.Select(_ => $"provider-{_.Id}")))
        {
            var bytes = Images.Glyph(name, 32);
            if (bytes is null)
            {
                continue;
            }

            fixed (byte* pointer = bytes)
            {
                // ReSharper disable once RedundantUnsafeContext
                unsafe
                {
                    Bm.TraySetMenuIcon(name, pointer, bytes.Length);
                }
            }
        }

        return new NativeTray();
    }

    public void Apply(TrayModel model)
    {
    }

    /// <summary>
    /// Through osascript rather than UNUserNotificationCenter: that API refuses a process that is
    /// not an app bundle, and this tool is a bare executable.
    /// </summary>
    public void Notify(Notification notification) =>
        ProcessRunner.Run(
            "osascript",
            ["-e", $"display notification \"{Escape(notification.Message)}\" with title \"{Escape(notification.Title)}\""],
            timeout: TimeSpan.FromSeconds(5));

    static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public TrayInput Poll() =>
        new();

    public void Dispose()
    {
    }
}
