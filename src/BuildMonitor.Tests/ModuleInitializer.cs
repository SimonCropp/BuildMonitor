public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VersionReader.VersionString = "TheVersion";
        // Looser than the WinForms baselines: these come from CI rasterisers.
        VerifierSettings.UseSsimForPng(0.999);
    }
}
