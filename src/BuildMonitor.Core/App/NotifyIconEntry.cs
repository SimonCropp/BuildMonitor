/// <summary>
/// One tray icon Windows has seen: a subkey of HKCU\Control Panel\NotifyIconSettings.
/// <see cref="Promoted"/> is null until the user, or <see cref="TrayIconPromotion"/>, decides
/// between the taskbar and the overflow, and Windows puts an icon with no decision in the overflow.
/// </summary>
record NotifyIconEntry(string Key, string Path, bool? Promoted);
