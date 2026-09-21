/// <summary>
/// What happened at the tray icon since the last poll.
/// </summary>
/// <param name="ClickedNotification">A click on the notification that last popped, carrying the
/// <see cref="Notification.Key"/> it was built with, or "" where it named no single build. Null
/// where nothing was clicked, which is why an empty string and null differ here.</param>
readonly record struct TrayInput(string? ClickedItem = null, bool IconClicked = false, string? ClickedNotification = null);
