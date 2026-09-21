/// <summary>
/// Text waiting for the loop to put on the clipboard, and what the tray says once the window has
/// taken it or given up on it. The two travel as one value so that a copy which replaces this one
/// before the window takes it replaces what would have been said about it too: a triage prompt
/// overtaken by a log must not have the log announced as the prompt.
/// <para>
/// Compared by reference, as <see cref="ClipboardPump"/> and <see cref="MonitorSession.Copied"/>
/// compare it, never by value: two fetches of the same log are two copies, and the second must not
/// be taken as already done because the first was.
/// </para>
/// </summary>
/// <param name="Copied">Popped once the window has taken the text, and not before. Until then the
/// clipboard holds whatever was copied last, and a notification sent any earlier sends someone off
/// to paste that. Null for a copy the status line says enough about.</param>
/// <param name="Failed">Popped in its place when the window gives up on the text.</param>
record PendingCopy(string Text, Notification? Copied = null, Notification? Failed = null);
