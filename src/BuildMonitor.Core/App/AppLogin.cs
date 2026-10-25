/// <summary>
/// How BuildMonitor is named where the desktop starts it at login. The command is the tool's shim,
/// asked as the entry is written: see <see cref="ShimPath"/>.
/// </summary>
static class AppLogin
{
    public static LoginItem Item { get; } = new(
        "BuildMonitor",
        "Build and CI monitor",
        "buildmonitor",
        "com.simoncropp.buildmonitor",
        ShimPath.Resolve);
}
