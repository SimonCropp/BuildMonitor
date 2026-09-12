/// <summary>
/// The <see cref="IMonitorWindow"/> backed by the native library. The only type in the app that
/// touches it, so everything else stays testable on a machine with no display.
/// <para>
/// On macOS the tray rides on the same library, so its clicks arrive through
/// <see cref="Poll"/> too and <see cref="NativeTray"/> has nothing of its own to drain.
/// </para>
/// </summary>
sealed unsafe class NativeMonitorWindow : IMonitorWindow
{
    readonly ScreenPayload payload = new();
    bool disposed;

    NativeMonitorWindow()
    {
    }

    public static IMonitorWindow? Open(string title, int width, int height, bool hidden, out string? error)
    {
        error = null;
        int version;
        try
        {
            version = Bm.Version();
        }
        catch (DllNotFoundException exception)
        {
            error = $"Could not load the native renderer for this platform. {exception.Message}";
            return null;
        }
        catch (EntryPointNotFoundException exception)
        {
            error = $"The native renderer is missing an entry point. {exception.Message}";
            return null;
        }

        if (version != Bm.ExpectedVersion)
        {
            error = $"Native renderer version {version} does not match the expected {Bm.ExpectedVersion}.";
            return null;
        }

        if (!Init(title, width, height, hidden, EmbeddedFont.Bytes()))
        {
            error = "The native renderer could not open a window.";
            return null;
        }

        return new NativeMonitorWindow();
    }

    static bool Init(string title, int width, int height, bool hidden, byte[] font)
    {
        fixed (byte* bytes = font)
        {
            return Bm.Init(width, height, title, bytes, font.Length, 15f, hidden ? 1 : 0) == 1;
        }
    }

    public bool Present(Screen screen)
    {
        payload.Build(screen);
        return payload.Present() == 1;
    }

    public bool Capture(Screen screen, int width, int height, string pngPath)
    {
        payload.Build(screen);
        return payload.Capture(width, height, pngPath) == 1;
    }

    public MonitorInput Poll()
    {
        BmInput input;
        Bm.PollInput(&input);
        IReadOnlyList<FieldChange>? changes = null;
        if (input.ChangedField >= 0 &&
            input.ChangedField < payload.FieldIds.Count)
        {
            var value = input.ChangedValue is null || input.ChangedValueLength <= 0
                ? ""
                : Encoding.UTF8.GetString(input.ChangedValue, input.ChangedValueLength);
            changes = [new(payload.FieldIds[input.ChangedField], value)];
        }

        return new(
            Key: Key((BmKey) input.Key),
            ClickedButton: input.ClickedButton,
            ClickedRow: input.ClickedRow,
            ClickedLinkRow: input.ClickedLinkRow,
            ClickedLink: (LinkKind) input.ClickedLink,
            ClickedActionRow: input.ClickedActionRow,
            ClickedAction: (RowAction) input.ClickedAction,
            RightClickedRow: input.RightClickedRow,
            ClickedMenuItem: input.ClickedMenuItem,
            MenuClosed: input.MenuClosed != 0,
            FieldChanges: changes,
            ClickedField: input.ClickedField >= 0 && input.ClickedField < payload.FieldIds.Count ? payload.FieldIds[input.ClickedField] : null,
            ScrollDelta: input.ScrollDelta,
            ScrollTo: input.ScrollTo,
            CloseRequested: input.CloseRequested != 0,
            Columns: 120,
            Rows: Math.Max(1, input.Rows) + ScreenBuilder.Chrome,
            TrayItem: input.ClickedTrayItem >= 0 && input.ClickedTrayItem < payload.TrayItemIds.Count ? payload.TrayItemIds[input.ClickedTrayItem] : null,
            TrayIconClicked: input.TrayIconClicked != 0);
    }

    /// <summary>
    /// Explicit rather than a cast: the library reports only the keys a window can produce and
    /// the two enums deliberately do not line up.
    /// </summary>
    static CommandKind Key(BmKey key) =>
        key switch
        {
            BmKey.ScrollUp => CommandKind.ScrollUp,
            BmKey.ScrollDown => CommandKind.ScrollDown,
            BmKey.PageUp => CommandKind.PageUp,
            BmKey.PageDown => CommandKind.PageDown,
            BmKey.Home => CommandKind.ScrollHome,
            BmKey.End => CommandKind.ScrollEnd,
            BmKey.NextRow => CommandKind.NextRow,
            BmKey.PreviousRow => CommandKind.PreviousRow,
            BmKey.OpenBuild => CommandKind.OpenBuild,
            BmKey.Retry => CommandKind.Retry,
            BmKey.CancelBuild => CommandKind.Cancel,
            BmKey.Refresh => CommandKind.Refresh,
            BmKey.Copy => CommandKind.CopyBuildUrl,
            BmKey.Back => CommandKind.CancelForm,
            BmKey.Hide => CommandKind.Hide,
            BmKey.Quit => CommandKind.Quit,
            _ => CommandKind.None
        };

    public void SetHidden(bool hidden) =>
        Bm.SetHidden(hidden ? 1 : 0);

    public void Focus() =>
        Bm.Focus();

    public void SetClipboard(string text) =>
        Bm.SetClipboard(text);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Bm.Shutdown();
    }
}
