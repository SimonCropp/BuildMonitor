/// <summary>
/// Something the tray should tell the user about outside the window: a build that just failed, or
/// how something they started and may have left the window behind for turned out. Part of the state
/// and the frame, like everything else, so a snapshot shows what would have popped; the loop hands
/// it to the tray once and clears it.
/// </summary>
/// <param name="Key">The build it announced, so a click on it can land on that row. Null where
/// several failed at once and no single row is the one meant.</param>
record Notification(string Title, string Message, string? Key = null, NotificationKind Kind = NotificationKind.Error);
