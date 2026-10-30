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

    /// <summary>
    /// A head run from its copy on Windows has a path that says nothing about the shim, so it is
    /// told instead, and the path it is told wins over its own.
    /// </summary>
    [Test]
    [NotInParallel(nameof(ShimPath.Variable))]
    public async Task ShimPathPassedByTheLauncher()
    {
        var shim = Path.Combine("C:", "Users", "simon", ".dotnet", "tools", "buildmonitor.exe");
        Environment.SetEnvironmentVariable(ShimPath.Variable, shim);
        try
        {
            await Assert.That(ShimPath.Resolve()).IsEqualTo(shim);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ShimPath.Variable, null);
        }
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
    /// What BuildMonitor hands PlatformHeads' update, whose scripts are tested there, each run on
    /// its own platform: the package, where the outcome goes, the head's copy to stop processes
    /// under beside the store, and the store beside the shim, whatever the tools folder is.
    /// </summary>
    [Test]
    public async Task UpdaterIsBuildMonitorsToolUpdate()
    {
        var tools = Path.Combine(Path.GetTempPath(), "home", ".dotnet", "tools");
        var update = Updater.For(Path.Combine(tools, "buildmonitor"));

        await Assert.That(update.PackageId).IsEqualTo("BuildMonitor");
        await Assert.That(update.Outcome).IsEqualTo(AppPaths.UpdateOutcome);
        await Assert.That(update.AlsoStopUnder).IsEquivalentTo([AppPaths.Head]);
        await Assert.That(update.ToolPath).IsEqualTo(tools);
        await Assert.That(update.StoreDirectory).IsEqualTo(Path.Combine(tools, ".store", "BuildMonitor"));
    }

    [Test]
    public Task LaunchAgentPlist() =>
        Verify(LaunchAgent.Compose(AppLogin.Item, "/Users/simon/.dotnet/tools/buildmonitor"))
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
                    <key>AbandonProcessGroup</key>
                    <true/>
                </dict>
                </plist>

                """);

    [Test]
    public Task XdgDesktopEntry() =>
        Verify(XdgAutostart.Compose(AppLogin.Item, "/home/simon/.dotnet/tools/buildmonitor"))
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
