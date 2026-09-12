/// <summary>
/// What happened at the tray icon since the last poll.
/// </summary>
readonly record struct TrayInput(string? ClickedItem = null, bool IconClicked = false);
