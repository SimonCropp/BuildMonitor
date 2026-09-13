static class TrayApp
{
    static bool configured;

    public static void Configure()
    {
        if (configured)
        {
            return;
        }

        configured = true;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        // Decided once, here: SetColorMode after windows exist hangs, so a theme change on save
        // repaints the palette immediately but native parts (check boxes, scroll bars) follow on
        // the next start.
        Palette.Use(StartupTheme());
        Application.SetColorMode(Palette.Light ? SystemColorMode.Classic : SystemColorMode.Dark);
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
    public static void ConfigureUnscaled()
    {
        if (configured)
        {
            return;
        }

        configured = true;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        Application.SetColorMode(SystemColorMode.Dark);
    }
}
