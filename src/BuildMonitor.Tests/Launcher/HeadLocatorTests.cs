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
}
