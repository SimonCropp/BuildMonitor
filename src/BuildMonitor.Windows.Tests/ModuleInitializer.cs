public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifyWinForms.Initialize();
        // Effectively "the same pixels", rather than Verify's 0.98 default. These screens are
        // mostly flat background, so 0.98 is far looser than it sounds on them.
        VerifierSettings.UseSsimForPng(0.9999);
        // Unscaled, so the captures describe the app rather than the display of whoever
        // captured them.
        TrayApp.ConfigureUnscaled();
        VersionReader.VersionString = "TheVersion";
    }
}
