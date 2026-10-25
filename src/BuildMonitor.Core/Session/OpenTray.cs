/// <summary>
/// Opens the tray for one platform: PlatformHeads' tray with this app's names and pictures. Null
/// with an <paramref name="error"/> when the desktop has no tray to put an icon in, which on Linux
/// is ordinary: the app then runs windowed.
/// </summary>
delegate ITray? OpenTray(out string? error);
