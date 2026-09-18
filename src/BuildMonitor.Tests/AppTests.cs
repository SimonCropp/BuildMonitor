public class AppTests
{
    [Test]
    public async Task ShimPathFromAStorePath()
    {
        var head = Path.Combine("C:", "Users", "simon", ".dotnet", "tools", ".store", "buildmonitor", "1.0.0", "buildmonitor", "1.0.0", "tools", "net10.0", "any", "heads", "win-x64", "BuildMonitor.Tray.exe");
        var expected = Path.Combine("C:", "Users", "simon", ".dotnet", "tools", OperatingSystem.IsWindows() ? "buildmonitor.exe" : "buildmonitor");
        await Assert.That(ShimPath.Resolve(head)).IsEqualTo(expected);
    }

    /// <summary>
    /// The real scan, on whatever machine this runs on. What it finds is not the point and cannot
    /// be: it depends on what else is up. What is pinned is that it answers at all, because every
    /// read of a process here can throw for a process that has exited or belongs to someone else,
    /// and a throw would take the update page with it.
    /// </summary>
    [Test]
    public async Task ListingTheRunningServersAnswersOnThisPlatform()
    {
        var servers = McpServers.Find();

        await Assert.That(servers.StoppedByUpdate).IsEqualTo(OperatingSystem.IsWindows());
        await Assert.That(servers.Running.Any(_ => _.ProcessId == Environment.ProcessId)).IsFalse();
    }

    [Test]
    public async Task ShimPathOutsideTheStoreIsTheProcess()
    {
        var path = Path.Combine("D:", "Code", "bin", "BuildMonitor.Tray.exe");
        await Assert.That(ShimPath.Resolve(path)).IsEqualTo(path);
    }

    [Test]
    public async Task ToolStorePathFromAStorePath()
    {
        var head = Path.Combine("C:", "Users", "simon", ".dotnet", "tools", ".store", "buildmonitor", "1.0.0-beta.2", "buildmonitor", "1.0.0-beta.2", "tools", "net10.0", "any", "heads", "win-x64", "BuildMonitor.Tray.exe");
        var expected = new ToolStorePath(Path.Combine("C:", "Users", "simon", ".dotnet", "tools", ".store", "buildmonitor"), "1.0.0-beta.2", "BuildMonitor.Tray.exe");
        await Assert.That(ToolStorePath.Parse(head)).IsEqualTo(expected);
    }

    [Test]
    public async Task ToolStorePathOutsideTheStoreIsNull()
    {
        await Assert.That(ToolStorePath.Parse(Path.Combine("D:", "Code", "bin", "BuildMonitor.Tray.exe"))).IsNull();
        await Assert.That(ToolStorePath.Parse(Path.Combine("C:", "tools", ".store", "buildmonitor"))).IsNull();
    }

    [Test]
    public async Task IssueUrlEncodesTitleAndBody()
    {
        var url = IssueLauncher.BuildUrl("Fails on #main & branch");
        await Assert.That(url).StartsWith("https://github.com/SimonCropp/BuildMonitor/issues/new?title=Fails+on+%23main+%26+branch&body=");
        await Assert.That(url).Contains(WebUtility.UrlEncode("BuildMonitor version: TheVersion"));
        await Assert.That(url).DoesNotContain("#main");
    }

    [Test]
    public async Task IssueForExceptionOpensOnce()
    {
        var opened = new List<string>();
        var original = LinkLauncher.OpenUrl;
        LinkLauncher.OpenUrl = opened.Add;
        try
        {
            IssueLauncher.LaunchForException("Boom once", new InvalidOperationException("bad"));
            IssueLauncher.LaunchForException("Boom once", new InvalidOperationException("bad"));
            await Assert.That(opened.Count).IsEqualTo(1);
            await Assert.That(opened[0]).Contains(WebUtility.UrlEncode("InvalidOperationException"));
        }
        finally
        {
            LinkLauncher.OpenUrl = original;
        }
    }

    /// <summary>
    /// Which script this platform starts. The scripts themselves are pinned in the tests after this
    /// one, which run on every platform, where a branch here runs only on its own platform's job.
    /// </summary>
    [Test]
    public async Task UpdaterStartsThisPlatformsScript()
    {
        var shim = Path.Combine("tools", "buildmonitor");
        var outcome = Path.Combine("home", "update-outcome.log");
        var info = Updater.StartInfo(shim, outcome, 4242);
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(info.FileName).IsEqualTo("powershell.exe");
            var script = Encoding.Unicode.GetString(Convert.FromBase64String(info.ArgumentList.Last()));
            await Assert.That(script).IsEqualTo(Updater.WindowsScript(shim, outcome, 4242));
        }
        else if (OperatingSystem.IsMacOS())
        {
            await Assert.That(info.FileName).IsEqualTo("/bin/sh");
            await Assert.That(info.ArgumentList.Last()).IsEqualTo(Updater.UnixCommand(shim, outcome, 4242));
        }
        else
        {
            await Assert.That(info.FileName).IsEqualTo("/bin/sh");
            await Assert.That(info.ArgumentList.Last()).IsEqualTo(Updater.Detached(Updater.UnixCommand(shim, outcome, 4242)));
        }
    }

    /// <summary>
    /// Under a user name with a quote in it, so the quoting is pinned as well.
    /// </summary>
    [Test]
    public Task UpdaterWindowsScript() =>
        Verify(Updater.WindowsScript(@"C:\Users\O'Brien\.dotnet\tools\buildmonitor.exe", @"C:\Users\O'Brien\AppData\Local\BuildMonitor\update-outcome.log", 4242))
            .Snapshot("Wait-Process -Id 4242 -Timeout 30 -ErrorAction SilentlyContinue; Get-Process buildmonitor -ErrorAction SilentlyContinue | Where-Object Path -eq 'C:\\Users\\O''Brien\\.dotnet\\tools\\buildmonitor.exe' | Stop-Process -Force; $output = dotnet tool update BuildMonitor --global --prerelease 2>&1 | ForEach-Object { \"$_\" }; $status = if ($LASTEXITCODE -eq 0) { 'ok' } else { 'failed' }; Set-Content -LiteralPath 'C:\\Users\\O''Brien\\AppData\\Local\\BuildMonitor\\update-outcome.log' -Value (@($status) + $output) -Encoding UTF8; & 'C:\\Users\\O''Brien\\.dotnet\\tools\\buildmonitor.exe'");

    [Test]
    public Task UpdaterUnixCommand() =>
        Verify(Updater.UnixCommand("/home/me/.dotnet/tools/buildmonitor", "/home/me/.local/share/BuildMonitor/update-outcome.log", 4242))
            .Snapshot("waited=0; while kill -0 4242 2>/dev/null && [ $waited -lt 150 ]; do sleep 0.2; waited=$((waited+1)); done; output=$(dotnet tool update BuildMonitor --global --prerelease 2>&1) && status=ok || status=failed; printf '%s\\n%s\\n' \"$status\" \"$output\" > \"/home/me/.local/share/BuildMonitor/update-outcome.log\"; nohup \"/home/me/.dotnet/tools/buildmonitor\" >/dev/null 2>&1 &");

    /// <summary>
    /// The quotes the outcome's printf needs survive being wrapped in the quotes setsid's shell
    /// needs, alongside the one in the user name.
    /// </summary>
    [Test]
    public Task UpdaterDetachesOnLinux() =>
        Verify(Updater.Detached(Updater.UnixCommand("/home/o'brien/.dotnet/tools/buildmonitor", "/home/o'brien/.local/share/BuildMonitor/update-outcome.log", 4242)))
            .Snapshot("setsid sh -c 'waited=0; while kill -0 4242 2>/dev/null && [ $waited -lt 150 ]; do sleep 0.2; waited=$((waited+1)); done; output=$(dotnet tool update BuildMonitor --global --prerelease 2>&1) && status=ok || status=failed; printf '\\''%s\\n%s\\n'\\'' \"$status\" \"$output\" > \"/home/o'\\''brien/.local/share/BuildMonitor/update-outcome.log\"; nohup \"/home/o'\\''brien/.dotnet/tools/buildmonitor\" >/dev/null 2>&1 &'");

    [Test]
    public Task LaunchAgentPlist() =>
        Verify(LaunchAgent.Compose("/Users/simon/.dotnet/tools/buildmonitor"))
            .Snapshot(
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                <plist version="1.0">
                <dict>
                    <key>Label</key>
                    <string>com.simoncropp.buildmonitor</string>
                    <key>ProgramArguments</key>
                    <array>
                        <string>/Users/simon/.dotnet/tools/buildmonitor</string>
                    </array>
                    <key>RunAtLoad</key>
                    <true/>
                    <key>LimitLoadToSessionType</key>
                    <string>Aqua</string>
                </dict>
                </plist>

                """);

    [Test]
    public Task XdgDesktopEntry() =>
        Verify(XdgAutostart.Compose("/home/simon/.dotnet/tools/buildmonitor"))
            .Snapshot(
                """
                [Desktop Entry]
                Type=Application
                Name=BuildMonitor
                Comment=Build and CI monitor
                Exec="/home/simon/.dotnet/tools/buildmonitor"
                Terminal=false
                X-GNOME-Autostart-enabled=true

                """);

    [Test]
    public async Task RunKeyValueIsQuoted() =>
        await Assert.That(WindowsRunKey.Value(@"C:\Program Files\x\buildmonitor.exe")).IsEqualTo("\"C:\\Program Files\\x\\buildmonitor.exe\"");

    [Test]
    public async Task DtoSnapshotMatchesTheRows()
    {
        var state = Fixtures.WithBuilds();
        await Verify(new
        {
            builds = Snapshot.Builds(state, Fixtures.Now),
            summary = Snapshot.Summary(state, Fixtures.Now)
        });
    }

    [Test]
    public async Task SnapshotListsCollapsedPipelines()
    {
        var state = Fixtures.WithGreenProject();
        await Assert.That(Snapshot.Builds(state, Fixtures.Now).Count).IsEqualTo(8);
        var member = state.Builds.First(_ => _.PipelineId == "Verify/nuget.yml");
        await Assert.That(Snapshot.Find(state, member.Key, Fixtures.Now)).IsNotNull();
    }
}
