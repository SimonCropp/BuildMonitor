/// <summary>
/// The path that stays the same across updates: the dotnet tool shim in ~/.dotnet/tools. The
/// head runs from a versioned directory under .store, which the next update deletes, so a
/// Run-at-login entry or a relaunch that named the head would point at nothing after an update.
/// </summary>
static class ShimPath
{
    public const string Command = "buildmonitor";

    public static string Resolve(string? processPath = null)
    {
        processPath ??= Environment.ProcessPath ?? "";
        var segments = processPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var store = Array.IndexOf(segments, ".store");
        if (store <= 0)
        {
            return processPath;
        }

        var toolsDirectory = string.Join(Path.DirectorySeparatorChar, segments[..store]);
        return Path.Combine(toolsDirectory, OperatingSystem.IsWindows() ? $"{Command}.exe" : Command);
    }
}
