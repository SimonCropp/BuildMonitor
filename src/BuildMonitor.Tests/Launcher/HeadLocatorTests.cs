public class HeadLocatorTests
{
    [Test]
    public async Task ExactRidWinsThenOsArch()
    {
        using var root = new TempDirectory();
        var generic = Path.Combine(root, "heads", "linux-x64", "BuildMonitor.Tray");
        Directory.CreateDirectory(Path.GetDirectoryName(generic)!);
        await File.WriteAllTextAsync(generic, "");

        await Assert.That(HeadLocator.Find(root, ["linux-musl-x64", "linux-x64"], false)).IsEqualTo(generic);
        await Assert.That(HeadLocator.Find(root, ["linux-musl-x64"], false)).IsNull();

        var exact = Path.Combine(root, "heads", "linux-musl-x64", "BuildMonitor.Tray");
        Directory.CreateDirectory(Path.GetDirectoryName(exact)!);
        await File.WriteAllTextAsync(exact, "");
        await Assert.That(HeadLocator.Find(root, ["linux-musl-x64", "linux-x64"], false)).IsEqualTo(exact);
    }

    [Test]
    public async Task WindowsLooksForAnExe()
    {
        using var root = new TempDirectory();
        var head = Path.Combine(root, "heads", "win-x64", "BuildMonitor.Tray.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(head)!);
        await File.WriteAllTextAsync(head, "");
        await Assert.That(HeadLocator.Find(root, ["win-x64"], true)).IsEqualTo(head);
        await Assert.That(HeadLocator.Find(root, ["win-x64"], false)).IsNull();
    }

    [Test]
    public async Task RidsStartWithTheExactOne()
    {
        var rids = HeadLocator.Rids().ToList();
        await Assert.That(rids[0]).IsEqualTo(RuntimeInformation.RuntimeIdentifier);
        await Assert.That(rids.Count).IsGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// The probe for a musl RID is that RID alone. The linux-{arch} candidate after it names the
    /// glibc head, which cannot start on musl: the launcher would wait out its timeout for a tray
    /// that was never going to answer, rather than say there is no head for the machine.
    /// </summary>
    [Test]
    [Arguments("linux-musl-x64")]
    [Arguments("linux-musl-arm64")]
    public async Task MuslProbesItsOwnRidAndNothingElse(string runtimeIdentifier)
    {
        var rids = HeadLocator.Rids(runtimeIdentifier).ToList();
        await Assert.That(rids).IsEquivalentTo([runtimeIdentifier]);
    }

    /// <summary>
    /// Everywhere else the synthesised RID still follows the framework's own.
    /// </summary>
    [Test]
    public async Task OtherRidsStillFallBackToTheSynthesisedRid()
    {
        var rids = HeadLocator.Rids("some-rid").ToList();
        await Assert.That(rids.Count).IsGreaterThan(1);
        await Assert.That(rids[0]).IsEqualTo("some-rid");
    }
}
