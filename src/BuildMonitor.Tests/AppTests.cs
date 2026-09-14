public class AppTests
{
    [Test]
    public async Task ShimPathFromAStorePath()
    {
        var head = Path.Combine("C:", "Users", "simon", ".dotnet", "tools", ".store", "buildmonitor", "1.0.0", "buildmonitor", "1.0.0", "tools", "net10.0", "any", "heads", "win-x64", "BuildMonitor.Tray.exe");
        var expected = Path.Combine("C:", "Users", "simon", ".dotnet", "tools", OperatingSystem.IsWindows() ? "buildmonitor.exe" : "buildmonitor");
        await Assert.That(ShimPath.Resolve(head)).IsEqualTo(expected);
    }

    [Test]
    public async Task ShimPathOutsideTheStoreIsTheProcess()
    {
        var path = Path.Combine("D:", "Code", "bin", "BuildMonitor.Tray.exe");
        await Assert.That(ShimPath.Resolve(path)).IsEqualTo(path);
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

    [Test]
    public async Task UpdaterCommand()
    {
        var info = Updater.StartInfo(Path.Combine("home", "buildmonitor"));
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(info.FileName).IsEqualTo("powershell.exe");
            var encoded = info.ArgumentList.Last();
            var script = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
            await Verify(script)
                .UniqueForOSPlatform()
                .Snapshot("Start-Sleep -Seconds 2; dotnet tool update BuildMonitor --global --prerelease; & 'home\\buildmonitor'");
        }
        else
        {
            await Assert.That(info.FileName).IsEqualTo("/bin/sh");
            await Verify(info.ArgumentList.Last())
                .UniqueForOSPlatform();
        }
    }

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
