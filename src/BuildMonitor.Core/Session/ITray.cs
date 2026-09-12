/// <summary>
/// One platform's tray icon. <see cref="Apply"/> receives the whole <see cref="TrayModel"/>
/// every frame and is expected to diff it; <see cref="Poll"/> drains what the user did.
/// </summary>
interface ITray : IDisposable
{
    void Apply(TrayModel model);

    TrayInput Poll();
}

/// <summary>
/// Opens the tray for one platform. Null with an <paramref name="error"/> when the desktop has
/// no tray to put an icon in, which on Linux is ordinary: the app then runs windowed.
/// </summary>
delegate ITray? OpenTray(out string? error);
