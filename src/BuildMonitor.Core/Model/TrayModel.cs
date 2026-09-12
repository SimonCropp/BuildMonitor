/// <summary>
/// What the tray icon shows and offers. Part of the frame rather than a side channel, so the
/// snapshots describe it and the heads only display it.
/// </summary>
record TrayModel(TrayIconKind Icon, string Tooltip, IReadOnlyList<TrayMenuItem> Items);
