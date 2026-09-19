/// <summary>
/// Where the per user files live. Settable so tests point it at a temp directory, and
/// overridable with BuildMonitor_Home so a second profile can run beside the real one.
/// </summary>
static class AppPaths
{
    public const string Variable = "BuildMonitor_Home";

    public static string Directory { get; set; } = Default();

    static string Default()
    {
        var home = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrWhiteSpace(home))
        {
            return home;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildMonitor");
    }

    public static string Settings => Path.Combine(Directory, "settings.json");
    public static string History => Path.Combine(Directory, "history.json");
    public static string Secrets => Path.Combine(Directory, "secrets");

    /// <summary>
    /// Build artifacts and logs downloaded for a triage. Here rather than in the operating system's
    /// temp directory: these are files a prompt is about to name by path, and a cleaner that emptied
    /// temp between the prompt being composed and an assistant reading it would leave every path in
    /// that prompt naming nothing. <see cref="global::ArtifactStore"/> clears them instead.
    /// </summary>
    public static string Artifacts => Path.Combine(Directory, "artifacts");

    /// <summary>
    /// What the update wrote about how it went, for <see cref="global::UpdateOutcome"/> to report.
    /// Here rather than in the log directory, which sits inside the installed version and so is
    /// deleted by the next update that works.
    /// </summary>
    public static string UpdateOutcome => Path.Combine(Directory, "update-outcome.log");
}
