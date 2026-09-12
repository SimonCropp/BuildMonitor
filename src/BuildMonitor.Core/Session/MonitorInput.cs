/// <summary>
/// What the user did during the last frame, already translated out of native terms. Indexes are
/// into the visible slice of the last presented <see cref="Screen"/>, or -1.
/// </summary>
readonly record struct MonitorInput(
    CommandKind Key = CommandKind.None,
    int ClickedButton = -1,
    int ClickedRow = -1,
    // A click on a link chip of a visible row: which row and which chip.
    int ClickedLinkRow = -1,
    LinkKind ClickedLink = LinkKind.None,
    // A click on a Retry or Cancel chip of a visible row.
    int ClickedActionRow = -1,
    RowAction ClickedAction = RowAction.None,
    // A right-click on a visible row, or -1. Opens the context menu.
    int RightClickedRow = -1,
    // A click on an item of the open context menu, or -1.
    int ClickedMenuItem = -1,
    // The head's own menu was dismissed by the user rather than by a command.
    bool MenuClosed = false,
    // Edits made to form fields since the last poll, oldest first.
    IReadOnlyList<FieldChange>? FieldChanges = null,
    // A Button, Link or ListRow field that was clicked, by id.
    string? ClickedField = null,
    int ScrollDelta = 0,
    // An absolute first visible row, from a scrollbar, or -1.
    int ScrollTo = -1,
    bool CloseRequested = false,
    int Columns = 0,
    int Rows = 0,
    // A tray menu item that was clicked, by id.
    string? TrayItem = null,
    bool TrayIconClicked = false)
{
    /// <summary>
    /// Whether anything happened at all, which is what decides whether the status line's last
    /// message has been seen and can go.
    /// </summary>
    public bool Any =>
        Key != CommandKind.None ||
        ClickedButton >= 0 ||
        ClickedRow >= 0 ||
        ClickedLinkRow >= 0 ||
        ClickedActionRow >= 0 ||
        RightClickedRow >= 0 ||
        ClickedMenuItem >= 0 ||
        MenuClosed ||
        FieldChanges is { Count: > 0 } ||
        ClickedField is not null ||
        ScrollDelta != 0 ||
        ScrollTo >= 0 ||
        CloseRequested ||
        TrayItem is not null ||
        TrayIconClicked;
}
