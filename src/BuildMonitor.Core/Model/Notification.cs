/// <summary>
/// Something the tray should tell the user about outside the window: a build that just failed.
/// Part of the state and the frame, like everything else, so a snapshot shows what would have
/// popped; the loop hands it to the tray once and clears it.
/// </summary>
record Notification(string Title, string Message);
