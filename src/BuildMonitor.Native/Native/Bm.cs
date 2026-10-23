/// <summary>
/// The exports of libbuildmonitor_ui, one per entry in bm.h.
/// </summary>
static partial class Bm
{
    public const string Library = "buildmonitor_ui";

    /// <summary>
    /// Has the kernel's resolver find <see cref="Library"/> under runtimes/{rid}/native for these
    /// imports. Once a process, however many threads ask.
    /// </summary>
    public static void Resolve() =>
        NativeResolver.Register(typeof(Bm).Assembly, Library);

    /// <summary>
    /// Keep in sync with BM_VERSION in bm.h.
    /// </summary>
    public const int ExpectedVersion = 21;

    /// <summary>
    /// BmInput.ChangedField for an edit of the filter box, which is not one of BmScreen.Fields.
    /// Keep in sync with BM_SEARCH_FIELD in bm.h.
    /// </summary>
    public const int SearchField = -2;

    [LibraryImport(Library, EntryPoint = "bm_init", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Init(int width, int height, string title, byte* fontTtf, int fontLength, byte* emojiTtf, int emojiLength, float fontSize, BmPlacement* placement, int hidden);

    [LibraryImport(Library, EntryPoint = "bm_present")]
    public static partial int Present(BmScreen* screen);

    [LibraryImport(Library, EntryPoint = "bm_poll_input")]
    public static partial void PollInput(BmInput* input);

    [LibraryImport(Library, EntryPoint = "bm_capture", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Capture(BmScreen* screen, int width, int height, string pngPath);

    [LibraryImport(Library, EntryPoint = "bm_set_hidden")]
    public static partial void SetHidden(int hidden);

    [LibraryImport(Library, EntryPoint = "bm_focus")]
    public static partial void Focus();

    [LibraryImport(Library, EntryPoint = "bm_set_clipboard", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetClipboard(string text);

    [LibraryImport(Library, EntryPoint = "bm_pick_directory", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int PickDirectory(string? start, byte* buffer, int bufferLength);

    [LibraryImport(Library, EntryPoint = "bm_tray_available")]
    public static partial int TrayAvailable();

    [LibraryImport(Library, EntryPoint = "bm_tray_init")]
    public static partial int TrayInit();

    [LibraryImport(Library, EntryPoint = "bm_tray_set_icon")]
    public static partial void TraySetIcon(int kind, byte* png, int length);

    [LibraryImport(Library, EntryPoint = "bm_tray_set_menu_icon", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void TraySetMenuIcon(string name, byte* png, int length);

    [LibraryImport(Library, EntryPoint = "bm_set_row_icon", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetRowIcon(string name, byte* png, int length);

    [LibraryImport(Library, EntryPoint = "bm_shutdown")]
    public static partial void Shutdown();

    [LibraryImport(Library, EntryPoint = "bm_on_display_lost")]
    public static partial void OnDisplayLost(delegate* unmanaged<void> lost);

    [LibraryImport(Library, EntryPoint = "bm_version")]
    public static partial int Version();
}
