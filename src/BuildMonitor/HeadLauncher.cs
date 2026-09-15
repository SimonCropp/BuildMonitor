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

        if (await Start(port, cancel))
        {
            return 0;
        }

        return 2;
    }

    public static async Task<bool> Start(int port, Cancel cancel)
    {
        var head = HeadLocator.Find();
        if (head is null)
        {
            await Console.Error.WriteLineAsync($"No BuildMonitor head for {RuntimeInformation.RuntimeIdentifier} under {AppContext.BaseDirectory}");
            return false;
        }

        Launch(head);
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

    static void Launch(string head)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo(head)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(head)
            });
            return;
        }

        // nohup and the redirects detach the head from this shell, which exits at once.
        var info = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add($"nohup \"{head}\" >/dev/null 2>&1 &");
        using var shell = Process.Start(info);
        shell?.WaitForExit(5000);
    }
}
