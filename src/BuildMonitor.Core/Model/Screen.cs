/// <summary>
/// Everything needed to draw one frame, and nothing else. Built by <see cref="ScreenBuilder"/>,
/// rendered either as text by <see cref="AsciiRenderer"/> or as pixels by a head. Both consume
/// the identical structure, which is what makes the text snapshots meaningful.
/// <para>
/// Exactly one of <see cref="Builds"/> and <see cref="Form"/> is set, decided by
/// <see cref="Page"/>. <see cref="BuildsPage.Rows"/> holds only the visible slice, so a renderer
/// never decides what scrolls into view.
/// </para>
/// </summary>
record Screen(
    string Title,
    Page Page,
    BuildsPage? Builds,
    FormPage? Form,
    IReadOnlyList<Button> Buttons,
    string Status,
    TrayModel Tray,
    int Columns,
    int Rows,
    // The open context menu, or null. Anchored to a visible row.
    MenuOverlay? Menu = null,
    // What the tray should pop this frame, or null.
    Notification? Notification = null);
