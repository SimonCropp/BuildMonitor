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
        Application.SetColorMode(SystemColorMode.Dark);
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
