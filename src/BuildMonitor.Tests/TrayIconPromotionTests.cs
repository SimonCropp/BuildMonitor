/// <summary>
/// Where the tray icon goes: on the taskbar, or in the overflow Windows 11 puts every icon from a
/// path it has not seen, which every update is.
/// </summary>
public class TrayIconPromotionTests
{
    static readonly string tools = Path.Combine("C:", "Users", "simon", ".dotnet", "tools");

    static string Head(string version) =>
        Head(tools, version);

    static string Head(string toolsDirectory, string version) =>
        Path.Combine(toolsDirectory, ".store", "buildmonitor", version, "buildmonitor", version, "tools", "net10.0", "any", "heads", "win-x64", "BuildMonitor.Tray.exe");

    [Test]
    public async Task NothingUntilWindowsHasAnEntryForTheIcon() =>
        await Assert.That(TrayIconPromotion.Decide([new("1", Head("0.1.0-beta.6"), true)], Head("0.1.0-beta.9"))).IsNull();

    [Test]
    public async Task ARecordedDecisionIsKept()
    {
        NotifyIconEntry[] entries =
        [
            new("1", Head("0.1.0-beta.6"), true),
            new("2", Head("0.1.0-beta.9"), false)
        ];
        await Assert.That(TrayIconPromotion.Decide(entries, Head("0.1.0-beta.9"))).IsEmpty();
    }

    /// <summary>
    /// What a machine had after updating: beta.6 put on the taskbar by the user, and beta.9 in the
    /// overflow because Windows had not seen its path.
    /// </summary>
    [Test]
    public async Task AnUpdateTakesTheDecisionOfTheVersionBefore()
    {
        NotifyIconEntry[] entries =
        [
            new("14470925567966284596", Head("0.1.0-beta.6"), true),
            new("13451188880126210905", Head("0.1.0-beta.9"), null)
        ];
        await Assert.That(TrayIconPromotion.Decide(entries, Head("0.1.0-beta.9")))
            .IsEquivalentTo([new NotifyIconEntry("13451188880126210905", Head("0.1.0-beta.9"), true)]);
    }

    [Test]
    public async Task AnIconMovedToTheOverflowStaysThereAfterAnUpdate()
    {
        NotifyIconEntry[] entries =
        [
            new("1", Head("0.1.0-beta.9"), false),
            new("2", Head("0.1.0-beta.10"), null)
        ];
        await Assert.That(TrayIconPromotion.Decide(entries, Head("0.1.0-beta.10")))
            .IsEquivalentTo([new NotifyIconEntry("2", Head("0.1.0-beta.10"), false)]);
    }

    /// <summary>
    /// Compared as text, beta.9 would be the newest. A version with no decision has nothing to pass on.
    /// </summary>
    [Test]
    public async Task TheNewestOtherVersionWithADecisionDecides()
    {
        NotifyIconEntry[] entries =
        [
            new("1", Head("0.1.0-beta.6"), true),
            new("2", Head("0.1.0-beta.10"), false),
            new("3", Head("0.1.0-beta.9"), true),
            new("4", Head("0.1.0-beta.12"), null),
            new("5", Head("0.1.0-beta.11"), null)
        ];
        await Assert.That(TrayIconPromotion.Decide(entries, Head("0.1.0-beta.11")))
            .IsEquivalentTo([new NotifyIconEntry("5", Head("0.1.0-beta.11"), false)]);
    }

    [Test]
    public async Task AFirstInstallGoesOnTheTaskbar() =>
        await Assert.That(TrayIconPromotion.Decide([new("1", Head("0.1.0-beta.9"), null)], Head("0.1.0-beta.9")))
            .IsEquivalentTo([new NotifyIconEntry("1", Head("0.1.0-beta.9"), true)]);

    [Test]
    public async Task ABuildOutsideTheStoreGoesOnTheTaskbar()
    {
        var build = Path.Combine("C:", "Code", "BuildMonitor", "src", "BuildMonitor.Windows", "bin", "Release", "net10.0", "BuildMonitor.Tray.exe");
        NotifyIconEntry[] entries =
        [
            new("1", Head("0.1.0-beta.9"), false),
            new("2", build, null)
        ];
        await Assert.That(TrayIconPromotion.Decide(entries, build))
            .IsEquivalentTo([new NotifyIconEntry("2", build, true)]);
    }

    [Test]
    public async Task OnlyOtherVersionsOfTheSameHeadPassOnADecision()
    {
        NotifyIconEntry[] entries =
        [
            new("1", Path.Combine(tools, "DiffEngineTray.exe"), false),
            new("2", Head(Path.Combine("D:", "tools"), "0.1.0-beta.6"), false),
            new("3", Path.Combine(tools, ".store", "other", "1.0.0", "other", "1.0.0", "tools", "net10.0", "any", "BuildMonitor.Tray.exe"), false),
            new("4", Path.Combine(tools, ".store", "buildmonitor", "0.1.0-beta.6", "buildmonitor", "0.1.0-beta.6", "tools", "net10.0", "any", "Other.exe"), false),
            new("5", Head("0.1.0-beta.9"), null)
        ];
        await Assert.That(TrayIconPromotion.Decide(entries, Head("0.1.0-beta.9")))
            .IsEquivalentTo([new NotifyIconEntry("5", Head("0.1.0-beta.9"), true)]);
    }

    [Test]
    public async Task PathsMatchWhateverTheirCase()
    {
        NotifyIconEntry[] entries =
        [
            new("1", Head("0.1.0-beta.6").ToUpperInvariant(), false),
            new("2", Head("0.1.0-beta.9").ToUpperInvariant(), null)
        ];
        await Assert.That(TrayIconPromotion.Decide(entries, Head("0.1.0-beta.9")))
            .IsEquivalentTo([new NotifyIconEntry("2", Head("0.1.0-beta.9").ToUpperInvariant(), false)]);
    }

    [Test]
    [Arguments(@"C:\Users\simon\.dotnet\tools\DiffEngineTray.exe")]
    [Arguments(@"{not a folder}\App.exe")]
    [Arguments(@"{6D809377-6AF0-444B-8957-A3773F02200E\App.exe")]
    public async Task APathNamingNoKnownFolderIsLeftAsItIs(string path) =>
        await Assert.That(NotifyIconSettings.Expand(path)).IsEqualTo(path);

    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task AKnownFolderIsExpanded()
    {
        await Assert.That(NotifyIconSettings.Expand(@"{F38BF404-1D43-42F2-9305-67DE0B28FC23}\explorer.exe"))
            .IsEqualTo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
        // A well formed id that names no folder.
        await Assert.That(NotifyIconSettings.Expand(@"{00000000-0000-0000-0000-000000000001}\App.exe"))
            .IsEqualTo(@"{00000000-0000-0000-0000-000000000001}\App.exe");
    }
}
