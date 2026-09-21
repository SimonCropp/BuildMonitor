/// <summary>
/// What the user did during the last frame, already translated out of native terms. Indexes are
/// into the visible slice of the last presented <see cref="Screen"/>, or -1.
/// </summary>
readonly record struct MonitorInput(
    CommandKind Key = CommandKind.None,
    int ClickedButton = -1,
    int ClickedRow = -1,
    // A click on a chip, or the provider icon, of a visible row: which row and which chip.
    int ClickedChipRow = -1,
    ChipKind ClickedChip = ChipKind.None,
    // A click on the overflow chip of a visible row, and the first of the chips it stands in for.
    int ClickedOverflowRow = -1,
    ChipKind OverflowFrom = ChipKind.None,
    // A right-click on a visible row, or -1. Opens the context menu.
    int RightClickedRow = -1,
    // A click on an item of the open context menu, or -1.
    int ClickedMenuItem = -1,
    // The head's own menu was dismissed by the user rather than by a command.
    bool MenuClosed = false,
    // Edits made to form fields since the last poll, oldest first.
    IReadOnlyList<FieldChange>? FieldChanges = null,
    // A Button, Link, ListRow or EditRow field that was clicked, by id.
    string? ClickedField = null,
    // The filter box's text when it was edited, or null.
    string? Search = null,
    int ScrollDelta = 0,
    // An absolute first visible row, from a scrollbar, or -1.
    int ScrollTo = -1,
    bool CloseRequested = false,
    int Columns = 0,
    int Rows = 0,
    // A tray menu item that was clicked, by id.
    string? TrayItem = null,
    bool TrayIconClicked = false,
    // A click on the notification that last popped: the build it named, or "" where it named
    // several. Null where it was not clicked.
    string? ClickedNotification = null)
{
    /// <summary>
    /// Whether anything happened at all, which is what decides whether the status line's last
    /// message has been seen and can go.
    /// </summary>
    public bool Any =>
        Key != CommandKind.None ||
        ClickedButton >= 0 ||
        ClickedRow >= 0 ||
        ClickedChipRow >= 0 ||
        ClickedOverflowRow >= 0 ||
        RightClickedRow >= 0 ||
        ClickedMenuItem >= 0 ||
        MenuClosed ||
        FieldChanges is { Count: > 0 } ||
        ClickedField is not null ||
        Search is not null ||
        ScrollDelta != 0 ||
        ScrollTo >= 0 ||
        CloseRequested ||
        TrayItem is not null ||
        TrayIconClicked ||
        ClickedNotification is not null;
}
