/// <summary>
/// Against the real filesystem, as <see cref="SettingsHelperTests"/> is. The behaviours under test
/// are filesystem behaviours — a move onto an existing directory, a name Windows will not take, a
/// write time that decides a delete — and an in memory filesystem would assert against a model of
/// them rather than the thing itself.
/// <para>
/// The clock is a local the store closes over, so each test moves its own time rather than the
/// class's: a test that assigned instance data would leak that time into the next one.
/// </para>
/// </summary>
[NotInParallel(nameof(AppPaths))]
public class ArtifactStoreTests :
    IDisposable
{
    string original = AppPaths.Directory;
    string directory = Path.Combine(Path.GetTempPath(), $"BuildMonitorArtifacts_{Guid.NewGuid():N}");

    public ArtifactStoreTests() =>
        AppPaths.Directory = directory;

    static Build Build(string run = "77") =>
        Fixtures.Build("gh", "test.yml", "test.yml", "VerifyTests/DiffEngine", "main", run, BuildStatus.Failed);

    static Task Write(string staging, string name = "log.txt") =>
        File.WriteAllTextAsync(Path.Combine(staging, name), "contents");

    static IEnumerable<string> FileNames(string directory) =>
        Directory.GetFiles(directory).Select(_ => Path.GetFileName(_));

    /// <summary>
    /// The one real hazard in the design: a download holds its directory open for minutes, and a
    /// sweep that ran meanwhile would delete the files out from under it.
    /// </summary>
    [Test]
    public async Task ASweepLeavesABundleBeingWrittenAlone()
    {
        var now = Fixtures.Now;
        var store = new ArtifactStore(() => now);
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var writing = store.Write(
            Build(),
            async staging =>
            {
                started.SetResult();
                await release.Task;
                await Write(staging);
            });
        await started.Task;
        now = now.AddHours(25);
        store.Sweep();
        release.SetResult();
        var final = await writing;
        await Assert.That(File.Exists(Path.Combine(final, "log.txt"))).IsTrue();
    }

    [Test]
    public async Task AFinishedBundleIsSweptOnceItIsADayOld()
    {
        var now = Fixtures.Now;
        var store = new ArtifactStore(() => now);
        var final = await store.Write(Build(), _ => Write(_));
        now = now.AddHours(25);
        store.Sweep();
        await Assert.That(Directory.Exists(final)).IsFalse();
    }

    [Test]
    public async Task AFreshBundleIsLeftAlone()
    {
        var now = Fixtures.Now;
        var store = new ArtifactStore(() => now);
        var final = await store.Write(Build(), _ => Write(_));
        now = now.AddHours(23);
        store.Sweep();
        await Assert.That(Directory.Exists(final)).IsTrue();
    }

    [Test]
    public async Task ASweepDoesNotMindTheDirectoryBeingAbsent()
    {
        new ArtifactStore().Sweep();
        await Assert.That(Directory.Exists(AppPaths.Artifacts)).IsFalse();
    }

    /// <summary>
    /// A second triage must replace what it finds rather than mix its files among the last
    /// attempt's, which would hand an assistant a file list that never existed on the service.
    /// </summary>
    [Test]
    public async Task ASecondTriageOfOneRunReplacesTheFirst()
    {
        var store = new ArtifactStore();
        var first = await store.Write(Build(), _ => Write(_, "old.txt"));
        var second = await store.Write(Build(), _ => Write(_, "new.txt"));
        await Assert.That(second).IsEqualTo(first);
        await Assert.That(FileNames(second)).IsEquivalentTo(["new.txt"]);
    }

    /// <summary>
    /// A retry is a different run on the same row, and its files must not arrive under the name of
    /// the run before it.
    /// </summary>
    [Test]
    public async Task ARetryGetsItsOwnDirectory() =>
        await Assert.That(ArtifactStore.DirectoryFor(Build())).IsNotEqualTo(ArtifactStore.DirectoryFor(Build("78")));

    [Test]
    public async Task AFailedWriteLeavesNothingBehind()
    {
        var store = new ArtifactStore();
        await Assert.That(async () => await store.Write(Build(), _ => throw new InvalidOperationException("nope")))
            .Throws<InvalidOperationException>();
        await Assert.That(Directory.Exists(ArtifactStore.DirectoryFor(Build()))).IsFalse();
        await Assert.That(Directory.GetDirectories(AppPaths.Artifacts)).IsEmpty();
    }

    /// <summary>
    /// A staging directory a killed run left behind is reaped on age like anything else, so a crash
    /// mid download does not leave files sitting there forever.
    /// </summary>
    [Test]
    public async Task AbandonedStagingIsSweptToo()
    {
        var now = Fixtures.Now;
        var store = new ArtifactStore(() => now);
        var abandoned = $"{ArtifactStore.DirectoryFor(Build())}.abcdef.partial";
        Directory.CreateDirectory(abandoned);
        Directory.SetLastWriteTimeUtc(abandoned, now.UtcDateTime);
        now = now.AddHours(25);
        store.Sweep();
        await Assert.That(Directory.Exists(abandoned)).IsFalse();
    }

    [Test]
    [Arguments("VerifyTests/Verify", "VerifyTests-Verify")]
    [Arguments("feature/inline snapshots", "feature-inline-snapshots")]
    [Arguments("../../etc/passwd", "etc-passwd")]
    [Arguments("trailing.", "trailing")]
    [Arguments("///", "file")]
    [Arguments("", "file")]
    public async Task NamesAFilesystemWillTake(string text, string expected) =>
        await Assert.That(ArtifactStore.Safe(text)).IsEqualTo(expected);

    /// <summary>
    /// Two rows that shorten to the same readable stem still have to land in different directories,
    /// which is what the hash after the stem is for.
    /// </summary>
    [Test]
    public async Task TwoRowsWithOneNameStillDiffer()
    {
        var main = Build() with
        {
            Branch = "main"
        };
        var feature = Build() with
        {
            Branch = "feature/very-long-branch-name-that-truncates"
        };
        await Assert.That(ArtifactStore.DirectoryFor(main)).IsNotEqualTo(ArtifactStore.DirectoryFor(feature));
    }

    [Test]
    public async Task ADirectoryNameIsReadableAndBounded()
    {
        var name = Path.GetFileName(ArtifactStore.DirectoryFor(Build()));
        await Assert.That(name).StartsWith("DiffEngine-77-");
        await Assert.That(name.Length).IsLessThanOrEqualTo(50);
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
