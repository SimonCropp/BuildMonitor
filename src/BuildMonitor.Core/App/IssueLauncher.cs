/// <summary>
/// Opens a new GitHub issue with the version and platform filled in. Both the title and the
/// body are URL encoded: a # in a title starts a fragment and an &amp; ends it.
/// </summary>
static class IssueLauncher
{
    const string repository = "https://github.com/SimonCropp/BuildMonitor";
    static readonly ConcurrentDictionary<string, byte> raised = new();

    public static string BuildUrl(string title, string extraBody = "")
    {
        var body = $"""
             * BuildMonitor version: {VersionReader.VersionString}
             * OS: {Environment.OSVersion.VersionString}
             * Logs: {Logging.LogsDirectory}
            {extraBody}
            """;
        return $"{repository}/issues/new?title={WebUtility.UrlEncode(title)}&body={WebUtility.UrlEncode(body)}";
    }

    public static void Launch() =>
        LinkLauncher.OpenUrl(BuildUrl("TODO"));

    /// <summary>
    /// Once per message per run, so a failure that repeats every poll does not open a browser
    /// tab every poll.
    /// </summary>
    public static void LaunchForException(string message, Exception exception)
    {
        if (!raised.TryAdd(message, 0))
        {
            return;
        }

        var extra = $"""

             * Action: {message}
             * Exception:
            ```
            {exception}
            ```
            """;
        LinkLauncher.OpenUrl(BuildUrl(message, extra));
    }
}
