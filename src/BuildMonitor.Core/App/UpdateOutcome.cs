/// <summary>
/// How the last update went, for the tray it started again to report. The update runs in a shell
/// nobody sees, after the tray has exited, so without this there is nothing to see at either end of
/// it: the icon goes away and one comes back, and whether that is the new version or the old one
/// after a failure looks exactly the same.
/// </summary>
static class UpdateOutcome
{
    /// <summary>
    /// The first line the script writes, which says which of the two the rest of the file is.
    /// </summary>
    public const string Succeeded = "ok";
    public const string Failed = "failed";

    /// <summary>
    /// The notification for the outcome <paramref name="path"/> holds, or null when there is none,
    /// which is every start that did not follow an update. The file is removed once read, so an
    /// outcome is reported once rather than at every start, and the whole output goes to the log,
    /// since a notification has room for a line of it.
    /// </summary>
    public static Notification? Take(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            text = File.ReadAllText(path);
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning(exception, "Could not read the update outcome at {Path}", path);
            return null;
        }

        var (status, output) = Split(text);
        if (status == Succeeded)
        {
            Log.Information("Update finished. dotnet tool update said:\n{Output}", output);
            return Describe();
        }

        Log.Warning("Update failed, so {Version} is still the one installed. dotnet tool update said:\n{Output}", VersionReader.VersionString, output);
        return Describe(output);
    }

    /// <summary>
    /// The version rather than what the tool said, because this is the tray the update started: it
    /// is the installed one, whether the update moved it or found it already current.
    /// </summary>
    public static Notification Describe() =>
        new("BuildMonitor updated", $"Now running {VersionReader.VersionString}.");

    public static Notification Describe(string output) =>
        new("Update failed", Reason(output));

    /// <summary>
    /// A file with no status line at all is a failure: that is what the script wrote before it
    /// reported anything but one, and the whole of it is the output.
    /// </summary>
    static (string Status, string Output) Split(string text)
    {
        var lines = text.Split('\n');
        var first = lines[0].Trim();
        if (first != Succeeded &&
            first != Failed)
        {
            return (Failed, text);
        }

        return (first, string.Join('\n', lines.Skip(1)));
    }

    /// <summary>
    /// The last line dotnet tool update wrote, which is where it puts the cause, under a first line
    /// that only says the update failed.
    /// </summary>
    static string Reason(string output)
    {
        var last = output
            .Split('\n')
            .Select(_ => _.Trim())
            .LastOrDefault(_ => _.Length > 0);
        if (last is null)
        {
            return "dotnet tool update failed without saying why.";
        }

        return last;
    }
}
