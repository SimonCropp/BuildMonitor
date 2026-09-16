[NotInParallel(nameof(AppPaths))]
public class SettingsHelperTests :
    IDisposable
{
    readonly string original = AppPaths.Directory;
    readonly string directory = Path.Combine(Path.GetTempPath(), $"BuildMonitorSettings_{Guid.NewGuid():N}");

    public SettingsHelperTests() =>
        AppPaths.Directory = directory;

    [Test]
    public async Task RoundTrip()
    {
        var settings = Fixtures.Settings() with
        {
            RunAtStartup = true,
            PollIntervalSeconds = 45,
            CodeDirectory = "/code",
            Filters = [new(FilterKind.Suffix, FilterTarget.Branch, "-wip")]
        };
        await SettingsHelper.Write(settings);
        var read = SettingsHelper.Read();
        await Verify(read);
    }

    [Test]
    public async Task WrittenJsonHoldsNoToken()
    {
        await SettingsHelper.Write(Fixtures.Settings());
        var json = await File.ReadAllTextAsync(AppPaths.Settings);
        await Verify(json);
    }

    [Test]
    public async Task MissingFileIsDefaults()
    {
        var read = SettingsHelper.Read();
        await Assert.That(read.PollIntervalSeconds).IsEqualTo(30);
        await Assert.That(read.Connections).IsEmpty();
    }

    [Test]
    public async Task NoTempFileIsLeftBehind()
    {
        await SettingsHelper.Write(new());
        await Assert.That(File.Exists($"{AppPaths.Settings}.tmp")).IsFalse();
    }

    [Test]
    public async Task UnknownPropertiesAreIgnored()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(AppPaths.Settings, """{"PollIntervalSeconds": 12, "Future": true}""");
        var read = SettingsHelper.Read();
        await Assert.That(read.PollIntervalSeconds).IsEqualTo(12);
    }

    [Test]
    public async Task AFileWrittenBeforeHistoryDaysGetsTheDefault()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(AppPaths.Settings, """{"PollIntervalSeconds": 12}""");
        var read = SettingsHelper.Read();
        await Assert.That(read.HistoryDays).IsEqualTo(30);
    }

    /// <summary>
    /// A string the file does not name reads as null rather than the empty the initializer says,
    /// which is what every scan and every options page would then have to guard against.
    /// </summary>
    [Test]
    public async Task AFileWrittenBeforeCodeDirectoryReadsAsEmpty()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(AppPaths.Settings, """{"PollIntervalSeconds": 12}""");
        var read = SettingsHelper.Read();
        await Assert.That(read.CodeDirectory).IsEqualTo("");
    }

    public void Dispose()
    {
        AppPaths.Directory = original;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
