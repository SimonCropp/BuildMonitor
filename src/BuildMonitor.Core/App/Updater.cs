/// <summary>
/// Updates the tool and relaunches it. The update has to run after this process has exited on
/// Windows, where a running executable cannot be replaced, so a shell is started that waits,
/// updates and starts the shim again; this process then exits at once.
/// <para>
/// Nobody sees that shell, so a failure is written to <see cref="AppPaths.FailedUpdate"/> for the
/// tray it starts to report, see <see cref="FailedUpdate"/>. The shim is started again whether the
/// update worked or not: a failed update should leave the old version running, not no tray at all.
/// </para>
/// </summary>
static class Updater
{
    public const string PackageId = "BuildMonitor";

    public static void Run(Action exit)
    {
        Start();
        exit();
    }

    public static void Start()
    {
        var info = StartInfo(ShimPath.Resolve(), AppPaths.FailedUpdate);
        Log.Information("Updating with {File} {Arguments}", info.FileName, string.Join(' ', info.ArgumentList));
        using var process = Process.Start(info);
    }

    public static ProcessStartInfo StartInfo(string shim, string failure)
    {
        if (OperatingSystem.IsWindows())
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsScript(shim, failure)));
            var info = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-ExecutionPolicy");
            info.ArgumentList.Add("Unrestricted");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(encoded);
            return info;
        }

        var command = UnixCommand(shim, failure);
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
    /// Stops the MCP servers first. Each is the shim running <c>buildmonitor mcp</c> for an AI
    /// client, and a running one holds the installed version's files, so the update could not
    /// remove them however long it waited. They are matched on the shim's path, which leaves a
    /// build run from anywhere else alone. A client sees its server stop, and connecting it again
    /// starts the new version.
    /// </summary>
    public static string WindowsScript(string shim, string failure)
    {
        var quotedShim = Quote(shim);
        string[] steps =
        [
            "Start-Sleep -Seconds 2",
            $"Get-Process {ShimPath.Command} -ErrorAction SilentlyContinue | Where-Object Path -eq {quotedShim} | Stop-Process -Force",
            $$"""$output = dotnet tool update {{PackageId}} --global --prerelease 2>&1 | ForEach-Object { "$_" }""",
            $$"""if ($LASTEXITCODE -ne 0) { Set-Content -LiteralPath {{Quote(failure)}} -Value $output -Encoding UTF8 }""",
            $"& {quotedShim}"
        ];
        return string.Join("; ", steps);
    }

    static string Quote(string path) =>
        $"'{path.Replace("'", "''")}'";

    /// <summary>
    /// A running file can be replaced here, so the MCP servers are left alone. They stay on the
    /// version they started with until their clients connect them again.
    /// </summary>
    public static string UnixCommand(string shim, string failure) =>
        $"""sleep 2; output=$(dotnet tool update {PackageId} --global --prerelease 2>&1) || printf '%s\n' "$output" > "{failure}"; nohup "{shim}" >/dev/null 2>&1 &""";

    /// <summary>
    /// setsid detaches the shell from this process so exiting does not take it down; nohup and the
    /// redirects keep the relaunched tray alive after the shell ends.
    /// </summary>
    public static string Detached(string command) =>
        $"setsid sh -c '{command.Replace("'", "'\\''")}'";
}
