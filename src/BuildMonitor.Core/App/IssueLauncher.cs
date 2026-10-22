/// <summary>
/// Opens a new GitHub issue with the version and platform filled in: see <see cref="IssueLink"/>.
/// </summary>
static class IssueLauncher
{
    static readonly IssueLink link = new("https://github.com/SimonCropp/BuildMonitor");

    public static string BuildUrl(string title, string extraBody = "") =>
        link.Url(
            title,
            $"""
             * BuildMonitor version: {VersionReader.VersionString}
             * OS: {Environment.OSVersion.VersionString}
             * Logs: {Logging.LogsDirectory}
            {extraBody}
            """);

    public static void Launch() =>
        LinkLauncher.OpenUrl(BuildUrl("TODO"));

    /// <summary>
    /// Once per message per run, so a failure that repeats every poll does not open a browser
    /// tab every poll.
    /// </summary>
    public static void LaunchForException(string message, Exception exception)
    {
        if (!link.FirstTime(message))
        {
            return;
        }

        var extra =
            $"""

             * Action: {message}
             * Exception:
            ```
            {exception}
            ```
            """;
        LinkLauncher.OpenUrl(BuildUrl(message, extra));
    }
}
