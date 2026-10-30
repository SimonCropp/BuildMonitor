/// <summary>
/// Updates the tool and relaunches it, by PlatformHeads' <see cref="ToolUpdate"/>, which this was
/// until DiffEngineTray needed the same. A shell is started that waits for this process to exit,
/// updates and starts the shim again, and this process then exits at once.
/// <para>
/// Nobody sees that shell, so how it went is written to <see cref="AppPaths.UpdateOutcome"/> for
/// the tray it starts to report, see <see cref="UpdateOutcome"/>. The shim is started again whether
/// the update worked or not: a failed update should leave the old version running, not no tray at
/// all.
/// </para>
/// </summary>
static class Updater
{
    public const string PackageId = "BuildMonitor";

    /// <summary>
    /// With the head's copy on Windows as well as the store to stop processes under: a head left
    /// over from a tray that outlasted the wait holds <see cref="HeadCopy"/> rather than the store.
    /// Left running, it would keep the port, so the shim started at the end would show it rather
    /// than start the new version, and keep the copy's files open, so the new version could not be
    /// copied over them. The store's processes are the shim running <c>buildmonitor mcp</c> for an
    /// AI client, among anything else started out of it, which <see cref="McpServers.Find"/>
    /// matches on too, so the update page warns about every server this stops.
    /// </summary>
    public static ToolUpdate For(string shim) =>
        new(PackageId, shim, AppPaths.UpdateOutcome)
        {
            AlsoStopUnder = [AppPaths.Head]
        };

    /// <summary>
    /// Starts the shell and returns. Exiting is the caller's, and has to be: the shell waits for
    /// this process to go before it updates, and a tray still running is the tray whose files the
    /// update then cannot replace.
    /// </summary>
    public static void Start()
    {
        var update = For(ShimPath.Resolve());
        Log.Information("Updating {Shim} by {Dotnet}", update.Shim, update.Dotnet);
        update.Start();
    }
}
