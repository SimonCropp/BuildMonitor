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
    /// <summary>
    /// How long <see cref="PickDirectory"/> waits on the chooser between frames. One frame, so a
    /// window left up while the chooser is answered keeps up with the desktop.
    /// </summary>
    static readonly TimeSpan frame = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// Room for the path bm_pick_directory writes. Four times the longest macOS can make, and macOS
    /// is the only head with a panel of its own to write one, so nothing it can be answered with
    /// fails to fit.
    /// </summary>
    const int pathBytes = 4096;

    ScreenPayload payload = new();
    // The screen the payload was last built from.
    Screen? built;
    bool disposed;

    NativeMonitorWindow()
    {
    }

    /// <param name="placement">Ignored: the C ABI reports nothing of where the window is, so no
    /// placement is ever saved from here, and one saved by the Windows head is in pixels of a
    /// desktop this is not.</param>
    public static IMonitorWindow? Open(string title, int width, int height, WindowPlacement? placement, bool hidden, out string? error)
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

        if (!Init(title, width, height, hidden, EmbeddedFont.Bytes(), EmbeddedFont.Emoji()))
        {
            error = "The native renderer could not open a window.";
            return null;
        }

        SetRowIcons();
        return new NativeMonitorWindow();
    }

    /// <summary>
    /// Handed over once, after bm_init has made the context a texture needs, so a frame names an
    /// icon rather than carrying its pixels every time it is presented.
    /// </summary>
    static void SetRowIcons()
    {
        // Every picture a row can name, under the name it names it by: the two marks it leads its
        // cells with, and its chips, which are drawn as pictures rather than labels. The names are
        // what BuildRow.NameIcon, DetailIcon and RowChip.Icon carry, and what bm.cpp and
        // BuildsRenderer.swift look each image up by.
        foreach (var name in ProviderMarks.All
                     .Concat(RepoHosts.All)
                     .Concat(RowChips.Icons))
        {
            SetRowIcon(name);
        }
    }

    static void SetRowIcon(string name)
    {
        var bytes = Images.Glyph(name, 32);
        if (bytes is null)
        {
            return;
        }

        fixed (byte* pointer = bytes)
        {
            Bm.SetRowIcon(name, pointer, bytes.Length);
        }
    }

    static bool Init(string title, int width, int height, bool hidden, byte[] font, byte[] emoji)
    {
        fixed (byte* bytes = font)
        fixed (byte* emojiBytes = emoji)
        {
            return Bm.Init(width, height, title, bytes, font.Length, emojiBytes, emoji.Length, 17f, hidden ? 1 : 0) == 1;
        }
    }

    public bool Present(Screen screen)
    {
        // The loop hands back the same instance until the state changes or the clock ticks over,
        // and flattening it again every frame made about 1.4 MB of garbage a second.
        if (!ReferenceEquals(screen, built))
        {
            payload.Build(screen);
            built = screen;
        }

        return payload.Present() == 1;
    }

    public bool Capture(Screen screen, int width, int height, string pngPath)
    {
        payload.Build(screen);
        built = screen;
        return payload.Capture(width, height, pngPath) == 1;
    }

    public MonitorInput Poll()
    {
        BmInput input;
        Bm.PollInput(&input);
        var value = input.ChangedValue is null || input.ChangedValueLength <= 0
            ? ""
            : Encoding.UTF8.GetString(input.ChangedValue, input.ChangedValueLength);
        IReadOnlyList<FieldChange>? changes = null;
        if (input.ChangedField >= 0 &&
            input.ChangedField < payload.FieldIds.Count)
        {
            changes = [new(payload.FieldIds[input.ChangedField], value)];
        }

        return new(
            Key: Key((BmKey) input.Key),
            ClickedButton: input.ClickedButton,
            ClickedRow: input.ClickedRow,
            ClickedChipRow: input.ClickedChipRow,
            ClickedChip: (ChipKind) input.ClickedChip,
            ClickedOverflowRow: input.ClickedOverflowRow,
            OverflowFrom: (ChipKind) input.OverflowFrom,
            RightClickedRow: input.RightClickedRow,
            ClickedMenuItem: input.ClickedMenuItem,
            MenuClosed: input.MenuClosed != 0,
            FieldChanges: changes,
            ClickedField: input.ClickedField >= 0 &&
                          input.ClickedField < payload.FieldIds.Count ? payload.FieldIds[input.ClickedField] : null,
            Search: input.ChangedField == Bm.SearchField ? value : null,
            ScrollDelta: input.ScrollDelta,
            ScrollTo: input.ScrollTo,
            CloseRequested: input.CloseRequested != 0,
            Columns: 120,
            Rows: Math.Max(1, input.Rows) + ScreenBuilder.Chrome,
            TrayItem: input.ClickedTrayItem >= 0 &&
                      input.ClickedTrayItem < payload.TrayItemIds.Count ? payload.TrayItemIds[input.ClickedTrayItem] : null,
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
            BmKey.CopyStatus => CommandKind.CopyStatus,
            BmKey.CopyLog => CommandKind.CopyLog,
            BmKey.Triage => CommandKind.Triage,
            BmKey.OpenMenu => CommandKind.OpenMenu,
            _ => CommandKind.None
        };

    public void SetHidden(bool hidden) =>
        Bm.SetHidden(hidden ? 1 : 0);

    public void Focus() =>
        Bm.Focus();

    /// <summary>
    /// Always true: bm_set_clipboard returns void, and both backends own their pasteboard rather
    /// than asking a desktop that can refuse, the way Windows does. Answering with a guess at a
    /// failure would cost the text three attempts and an untrue status.
    /// </summary>
    public bool SetClipboard(string text)
    {
        Bm.SetClipboard(text);
        return true;
    }

    /// <summary>
    /// The library's own panel where it has one, which on macOS is what this has to be: a chooser
    /// run as a program of its own puts its window up from another process, so nothing pumps this
    /// head's events while it is up and the window beachballs in front of it within a second.
    /// bm_pick_directory is modal and pumps as it waits, which is what the WinForms head gets from
    /// ShowDialog.
    /// <para>
    /// -1 is a head with no panel, which is Linux. The desktop's chooser is another process there
    /// too, so it is waited for off the loop with frames still going out and input drained and
    /// dropped, which is what a modal means here.
    /// </para>
    /// </summary>
    public string? PickDirectory(string? start)
    {
        var buffer = new byte[pathBytes];
        int written;
        fixed (byte* pointer = buffer)
        {
            written = Bm.PickDirectory(start, pointer, buffer.Length);
        }

        if (written >= 0)
        {
            if (written == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(buffer, 0, written);
        }

        var picking = Task.Run(() => DirectoryPicker.Pick(start));
        while (!picking.Wait(frame))
        {
            payload.Present();
            BmInput dropped;
            Bm.PollInput(&dropped);
        }

        return picking.Result;
    }

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
