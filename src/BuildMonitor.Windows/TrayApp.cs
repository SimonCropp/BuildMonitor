/// <summary>
/// The process wide WinForms setup, through the shell's <see cref="FormsApp"/>, with this app's
/// palette chosen before the colour mode it decides.
/// </summary>
static class TrayApp
{
    public static void Configure()
    {
        // Decided once, here: SetColorMode after windows exist hangs, so a theme change on save
        // repaints the palette immediately but native parts (check boxes, scroll bars) follow on
        // the next start.
        var theme = StartupTheme();
        Palette.Use(theme);
        FormsApp.Configure(HighDpiMode.PerMonitorV2, Palette.Light ? SystemColorMode.Classic : SystemColorMode.Dark);
    }

    static Theme StartupTheme()
    {
        try
        {
            return SettingsHelper.Read().Theme;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not read the theme from settings");
            return Theme.System;
        }
    }

    /// <summary>
    /// For the snapshot tests: unscaled, so the captures describe the app rather than the display
    /// of whoever captured them.
    /// </summary>
    public static void ConfigureUnscaled() =>
        FormsApp.Configure(HighDpiMode.DpiUnaware, SystemColorMode.Dark);
}
