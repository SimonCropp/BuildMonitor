/// <summary>
/// Updates the tool and relaunches it. The update has to run after this process has exited on
/// Windows, where a running executable cannot be replaced, so a shell is started that waits,
/// updates and starts the shim again; this process then exits at once.
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
        var info = StartInfo(ShimPath.Resolve());
        Log.Information("Updating with {File} {Arguments}", info.FileName, string.Join(' ', info.ArgumentList));
        using var process = Process.Start(info);
    }

    public static ProcessStartInfo StartInfo(string shim)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = $"Start-Sleep -Seconds 2; dotnet tool update {PackageId} --global --prerelease; & '{shim.Replace("'", "''")}'";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
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

        // setsid detaches the shell from this process so exiting does not take it down; nohup
        // and the redirects keep the relaunched tray alive after the shell ends.
        var command = $"sleep 2; dotnet tool update {PackageId} --global --prerelease; nohup \"{shim}\" >/dev/null 2>&1 &";
        var unix = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        unix.ArgumentList.Add("-c");
        unix.ArgumentList.Add(OperatingSystem.IsMacOS() ? command : $"setsid sh -c '{command.Replace("'", "'\\''")}'");
        return unix;
    }
}
