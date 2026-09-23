/// <summary>
/// The exports of libbuildmonitor_ui, one per entry in bm.h.
/// </summary>
static partial class Bm
{
    const string library = "buildmonitor_ui";

    /// <summary>
    /// Keep in sync with BM_VERSION in bm.h.
    /// </summary>
    public const int ExpectedVersion = 17;

    /// <summary>
    /// BmInput.ChangedField for an edit of the filter box, which is not one of BmScreen.Fields.
    /// Keep in sync with BM_SEARCH_FIELD in bm.h.
    /// </summary>
    public const int SearchField = -2;

    [LibraryImport(library, EntryPoint = "bm_init", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Init(int width, int height, string title, byte* fontTtf, int fontLength, byte* emojiTtf, int emojiLength, float fontSize, BmPlacement* placement, int hidden);

    [LibraryImport(library, EntryPoint = "bm_present")]
    public static partial int Present(BmScreen* screen);

    [LibraryImport(library, EntryPoint = "bm_poll_input")]
    public static partial void PollInput(BmInput* input);

    [LibraryImport(library, EntryPoint = "bm_capture", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int Capture(BmScreen* screen, int width, int height, string pngPath);

    [LibraryImport(library, EntryPoint = "bm_set_hidden")]
    public static partial void SetHidden(int hidden);

    [LibraryImport(library, EntryPoint = "bm_focus")]
    public static partial void Focus();

    [LibraryImport(library, EntryPoint = "bm_set_clipboard", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetClipboard(string text);

    [LibraryImport(library, EntryPoint = "bm_pick_directory", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int PickDirectory(string? start, byte* buffer, int bufferLength);

    [LibraryImport(library, EntryPoint = "bm_tray_available")]
    public static partial int TrayAvailable();

    [LibraryImport(library, EntryPoint = "bm_tray_init")]
    public static partial int TrayInit();

    [LibraryImport(library, EntryPoint = "bm_tray_set_icon")]
    public static partial void TraySetIcon(int kind, byte* png, int length);

    [LibraryImport(library, EntryPoint = "bm_tray_set_menu_icon", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void TraySetMenuIcon(string name, byte* png, int length);

    [LibraryImport(library, EntryPoint = "bm_set_row_icon", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void SetRowIcon(string name, byte* png, int length);

    [LibraryImport(library, EntryPoint = "bm_shutdown")]
    public static partial void Shutdown();

    [LibraryImport(library, EntryPoint = "bm_version")]
    public static partial int Version();
}
