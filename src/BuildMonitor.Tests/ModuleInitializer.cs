public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize() =>
        VersionReader.VersionString = "TheVersion";
}
