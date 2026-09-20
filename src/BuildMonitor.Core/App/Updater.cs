/// <summary>
/// Updates the tool and relaunches it. The update has to run after this process has exited on
/// Windows, where a running executable cannot be replaced, so a shell is started that waits,
/// updates and starts the shim again; this process then exits at once.
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
    /// How long the shell waits for the tray to exit before updating anyway. Long enough for a
    /// shutdown that is waiting on a poll and the socket, short enough that a tray which is never
    /// going to exit does not leave the user with no update and no reason for it.
    /// </summary>
    const int waitSeconds = 30;

    /// <summary>
    /// How long the shell waits for the processes it killed to go. Stopping one is asked for
    /// rather than waited on, and the handles it holds are only released once it has gone.
    /// </summary>
    const int killSeconds = 5;

    /// <summary>
    /// Starts the shell and returns. Exiting is the caller's, and has to be: the shell waits for
    /// this process to go before it updates, and a tray still running is the tray whose files the
    /// update then cannot replace.
    /// </summary>
    public static void Start()
    {
        var info = StartInfo(ShimPath.Resolve(), AppPaths.UpdateOutcome, Environment.ProcessId);
        Log.Information("Updating with {File} {Arguments}", info.FileName, string.Join(' ', info.ArgumentList));
        using var process = Process.Start(info);
    }

    public static ProcessStartInfo StartInfo(string shim, string outcome, int processId)
    {
        if (OperatingSystem.IsWindows())
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsScript(shim, StoreDirectory(shim), outcome, processId)));
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                // The head runs with its own directory under .store as the working directory, and
                // this shell would inherit it. Windows will not delete a directory that is some
                // process's working directory, so the uninstall the update starts with would be
                // denied the very version it is replacing, by the shell doing the replacing.
                WorkingDirectory = Path.GetTempPath()
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-ExecutionPolicy");
            info.ArgumentList.Add("Unrestricted");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(encoded);
            return info;
        }

        var command = UnixCommand(shim, outcome, processId);
        var unix = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        unix.ArgumentList.Add("-c");
        unix.ArgumentList.Add(OperatingSystem.IsMacOS() ? command : Detached(command));
        return unix;
    }

    /// <summary>
    /// Waits for the tray to go, rather than for a guess at how long it takes. Shutting down can
    /// outlast any fixed sleep: a poll in flight is waited on for three seconds and the socket for
    /// two, and an update that started while the tray was still there is one that cannot replace
    /// the tray's own files. Silent where the process has already gone, which is the usual case.
    /// <para>
    /// Then kills whatever still holds the installed version, because the uninstall the update
    /// starts with deletes the whole version directory and one open file under it fails all of it,
    /// as "Access to the path ... is denied". That is the shim running <c>buildmonitor mcp</c> for
    /// an AI client, a head left over from a tray that outlasted the wait above, and anything else
    /// started out of the store. They are matched on the shim's path and on the store directory,
    /// the first being what <see cref="McpServers.Find"/> matches on, so the update page warns
    /// about every server this stops. A client sees its server stop, and connecting it again starts
    /// the new version.
    /// </para>
    /// </summary>
    public static string WindowsScript(string shim, string store, string outcome, int processId)
    {
        var quotedShim = Quote(shim);
        var underStore = Quote($@"{store}\");
        string[] steps =
        [
            $"Wait-Process -Id {processId} -Timeout {waitSeconds} -ErrorAction SilentlyContinue",
            $$"""$locking = @(Get-Process | Where-Object { $_.Path -and ($_.Path -eq {{quotedShim}} -or $_.Path.StartsWith({{underStore}}, 'OrdinalIgnoreCase')) })""",
            "$locking | Stop-Process -Force -ErrorAction SilentlyContinue",
            $"$locking | Wait-Process -Timeout {killSeconds} -ErrorAction SilentlyContinue",
            $$"""$output = dotnet tool update {{PackageId}} --global --prerelease 2>&1 | ForEach-Object { "$_" }""",
            $$"""$status = if ($LASTEXITCODE -eq 0) { '{{UpdateOutcome.Succeeded}}' } else { '{{UpdateOutcome.Failed}}' }""",
            $"Set-Content -LiteralPath {Quote(outcome)} -Value (@($status) + $output) -Encoding UTF8",
            $"& {quotedShim}"
        ];
        return string.Join("; ", steps);
    }

    static string Quote(string path) =>
        $"'{path.Replace("'", "''")}'";

    /// <summary>
    /// Where the tool store keeps every installed version of the package, which is the directory
    /// the update empties: {tools}/.store/{package}, beside the shim's own directory. Matched
    /// case-insensitively by the script, since the store names the directory in the case the
    /// package id was pushed in rather than the case asked for here.
    /// </summary>
    public static string StoreDirectory(string shim) =>
        Path.Combine(Path.GetDirectoryName(shim) ?? "", ".store", PackageId);

    /// <summary>
    /// A running file can be replaced here, so the MCP servers are left alone. They stay on the
    /// version they started with until their clients connect them again.
    /// <para>
    /// The tray is still waited for, for a different reason than on Windows: the shim started at
    /// the end binds the port, and a tray still holding it would take that start as a second
    /// instance and be asked to show itself, leaving the old version running and looking updated.
    /// The count bounds the wait the way -Timeout does on Windows.
    /// </para>
    /// </summary>
    public static string UnixCommand(string shim, string outcome, int processId) =>
        $"""waited=0; while kill -0 {processId} 2>/dev/null && [ $waited -lt {waitSeconds * 5} ]; do sleep 0.2; waited=$((waited+1)); done; output=$(dotnet tool update {PackageId} --global --prerelease 2>&1) && status={UpdateOutcome.Succeeded} || status={UpdateOutcome.Failed}; printf '%s\n%s\n' "$status" "$output" > "{outcome}"; nohup "{shim}" >/dev/null 2>&1 &""";

    /// <summary>
    /// setsid detaches the shell from this process so exiting does not take it down; nohup and the
    /// redirects keep the relaunched tray alive after the shell ends.
    /// </summary>
    public static string Detached(string command) =>
        $"setsid sh -c '{command.Replace("'", "'\\''")}'";
}
