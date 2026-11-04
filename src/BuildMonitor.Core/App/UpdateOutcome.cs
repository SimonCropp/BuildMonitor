/// <summary>
/// How the last update went, for the tray it started again to report, through PlatformHeads'
/// <see cref="ToolUpdateOutcome.Report"/>. The update runs in a shell nobody sees, after the tray
/// has exited, so without this there is nothing to see at either end of it: the icon goes away and
/// one comes back, and whether that is the new version or the old one after a failure looks
/// exactly the same.
/// </summary>
static class UpdateOutcome
{
    /// <summary>
    /// The notification for the outcome <paramref name="path"/> holds, or null when there is none,
    /// which is every start that did not follow an update. The file is removed once read, so an
    /// outcome is reported once rather than at every start, and the whole output goes to the log,
    /// since a notification has room for a line of it.
    /// </summary>
    public static Notification? Take(string path) =>
        ToolUpdateOutcome.Report(
            path,
            "BuildMonitor",
            VersionReader.VersionString,
            Taken,
            _ => Log.Warning(_, "Could not read the update outcome at {Path}", path));

    static void Taken(ToolUpdateOutcome outcome)
    {
        if (outcome.Succeeded)
        {
            Log.Information("Update finished. dotnet tool update said:\n{Output}", outcome.Output);
            return;
        }

        Log.Warning("Update failed, so {Version} is still the one installed. dotnet tool update said:\n{Output}", VersionReader.VersionString, outcome.Output);
    }
}
