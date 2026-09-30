/// <summary>
/// The path that stays the same across updates: the dotnet tool shim in ~/.dotnet/tools. The
/// head runs from a versioned directory under .store, which the next update deletes, so a
/// Run-at-login entry or a relaunch that named the head would point at nothing after an update.
/// <para>
/// On Windows the head runs from <see cref="HeadCopy"/> instead, whose path says nothing about
/// where the shim is, so the launcher passes it in <see cref="Variable"/>. A Run-at-login entry
/// or a relaunch that named the copy would start the version already copied, and never the one an
/// update installed.
/// </para>
/// </summary>
static class ShimPath
{
    public const string Command = "buildmonitor";
    public const string Variable = "BuildMonitor_Shim";

    public static string Resolve()
    {
        var passed = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrWhiteSpace(passed))
        {
            return passed;
        }

        return Resolve(Environment.ProcessPath ?? "");
    }

    public static string Resolve(string processPath)
    {
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
