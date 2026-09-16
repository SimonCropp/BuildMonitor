/// <summary>
/// Starts the tray head detached from this process, and waits until it answers on its port.
/// </summary>
static class HeadLauncher
{
    static readonly TimeSpan startTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Shows the running tray, or starts one. Returns an exit code.
    /// </summary>
    public static async Task<int> StartOrShow(int port, bool show, Cancel cancel)
    {
        var client = new ProtocolClient(port);
        if (await client.IsRunning(cancel))
        {
            if (show)
            {
                await client.Send(new(Verb.Show), cancel);
            }

            return 0;
        }

        // A tray started only so that something can be asked of it opens no window: show says
        // whether a window was wanted, and it has to reach the head or the head falls back to
        // ShowWindowAtStart and takes the screen for a question the user did not ask on screen.
        if (await Start(port, cancel, hidden: !show))
        {
            return 0;
        }

        return 2;
    }

    public static async Task<bool> Start(int port, Cancel cancel, bool hidden = false)
    {
        var head = HeadLocator.Find();
        if (head is null)
        {
            await Console.Error.WriteLineAsync($"No BuildMonitor head for {RuntimeInformation.RuntimeIdentifier} under {AppContext.BaseDirectory}");
            return false;
        }

        Launch(head, hidden);
        var client = new ProtocolClient(port);
        var deadline = DateTime.UtcNow + startTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await client.IsRunning(cancel))
            {
                return true;
            }

            await Task.Delay(200, cancel);
        }

        await Console.Error.WriteLineAsync($"The tray did not answer on port {port} within {startTimeout.TotalSeconds} seconds");
        return false;
    }

    static void Launch(string head, bool hidden)
    {
        var arguments = hidden ? MonitorProgram.HiddenArgument : "";
        if (OperatingSystem.IsWindows())
        {
            var info = new ProcessStartInfo(head)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(head)
            };
            if (hidden)
            {
                info.ArgumentList.Add(MonitorProgram.HiddenArgument);
            }

            using var process = Process.Start(info);
            return;
        }

        // nohup and the redirects detach the head from this shell, which exits at once.
        var shellInfo = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        shellInfo.ArgumentList.Add("-c");
        shellInfo.ArgumentList.Add($"nohup \"{head}\" {arguments} >/dev/null 2>&1 &");
        using var shell = Process.Start(shellInfo);
        shell?.WaitForExit(5000);
    }
}
