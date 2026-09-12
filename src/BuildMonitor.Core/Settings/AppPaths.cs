/// <summary>
/// Where the per user files live. Settable so tests point it at a temp directory.
/// </summary>
static class AppPaths
{
    public static string Directory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildMonitor");

    public static string Settings => Path.Combine(Directory, "settings.json");
    public static string History => Path.Combine(Directory, "history.json");
    public static string Secrets => Path.Combine(Directory, "secrets");
}
