/// <summary>
/// What a failed update leaves for the tray it starts again. The update runs in a shell nobody
/// sees, after the tray has exited, and the tray it starts afterwards is whichever version is
/// still installed. Without this, an update that failed looks exactly like one that worked: the
/// icon comes back, and it is the old version.
/// </summary>
static class FailedUpdate
{
    /// <summary>
    /// The notification for the failure <paramref name="path"/> holds, or null when there is none.
    /// The file is removed once read, so a failure is reported once rather than at every start, and
    /// the whole output goes to the log, since a notification has room for a line of it.
    /// </summary>
    public static Notification? Take(string path)
    {
        string output;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            output = File.ReadAllText(path);
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning(exception, "Could not read the failed update at {Path}", path);
            return null;
        }

        Log.Warning("Update failed, so {Version} is still the one installed. dotnet tool update said:\n{Output}", VersionReader.VersionString, output);
        return Describe(output);
    }

    public static Notification Describe(string output) =>
        new("Update failed", Reason(output));

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
