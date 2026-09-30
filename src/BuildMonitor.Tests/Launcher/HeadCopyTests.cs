public class HeadCopyTests
{
    [Test]
    public async Task CopiesTheInstalledHead()
    {
        using var root = new TempDirectory();
        var head = await Install(root, "one");
        var target = Path.Combine(root, "copy");

        var started = HeadCopy.Prepare(head, target);

        await Assert.That(started).IsEqualTo(Path.Combine(target, "BuildMonitor.Tray.exe"));
        await Assert.That(await File.ReadAllTextAsync(started)).IsEqualTo("one");
        await Assert.That(File.Exists(Path.Combine(target, "runtimes", "native.dll"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(target, "logs"))).IsFalse();
    }

    /// <summary>
    /// The logs a head wrote while it ran from the store are that version's, and the copy keeps
    /// its own, which an update leaves where they are.
    /// </summary>
    [Test]
    public async Task AnUpdateReplacesTheFilesAndKeepsTheLogs()
    {
        using var root = new TempDirectory();
        var target = Path.Combine(root, "copy");
        HeadCopy.Prepare(await Install(root, "one"), target);
        var log = Path.Combine(target, "logs", "log.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        await File.WriteAllTextAsync(log, "kept");

        var started = HeadCopy.Prepare(await Install(root, "two"), target);

        await Assert.That(await File.ReadAllTextAsync(started)).IsEqualTo("two");
        await Assert.That(await File.ReadAllTextAsync(log)).IsEqualTo("kept");
    }

    /// <summary>
    /// A copy of a build from its bin directory would land on the installed one's.
    /// </summary>
    [Test]
    public async Task AHeadOutsideTheStoreStartsAsItIs()
    {
        using var root = new TempDirectory();
        var head = Path.Combine(root, "bin", "BuildMonitor.Tray.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(head)!);
        await File.WriteAllTextAsync(head, "");
        var target = Path.Combine(root, "copy");

        await Assert.That(HeadCopy.Prepare(head, target)).IsEqualTo(head);
        await Assert.That(Directory.Exists(target)).IsFalse();
    }

    /// <summary>
    /// A head still running from the copy holds its files open. Starting the installed head costs
    /// the icon's placement; failing to start would cost the tray.
    /// </summary>
    [Test]
    [RunOn(TUnit.Core.Enums.OS.Windows)]
    public async Task ACopyHeldOpenStartsTheInstalledHead()
    {
        using var root = new TempDirectory();
        var target = Path.Combine(root, "copy");
        var started = HeadCopy.Prepare(await Install(root, "one"), target);
        var head = await Install(root, "two");

        await using (File.Open(started, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.That(HeadCopy.Prepare(head, target)).IsEqualTo(head);
        }
    }

    /// <summary>
    /// A version laid out as the tool store lays one out, with the head's own log directory and a
    /// file in a subdirectory, as the head's runtimes are.
    /// </summary>
    static async Task<string> Install(string root, string version)
    {
        var directory = Path.Combine(root, "tools", ".store", "buildmonitor", version, "buildmonitor", version, "tools", "net10.0", "any", "heads", "win-x64");
        Directory.CreateDirectory(Path.Combine(directory, "runtimes"));
        Directory.CreateDirectory(Path.Combine(directory, "logs"));
        var head = Path.Combine(directory, "BuildMonitor.Tray.exe");
        await File.WriteAllTextAsync(head, version);
        await File.WriteAllTextAsync(Path.Combine(directory, "runtimes", "native.dll"), version);
        await File.WriteAllTextAsync(Path.Combine(directory, "logs", "log.txt"), version);
        return head;
    }
}
