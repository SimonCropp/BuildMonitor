/// <summary>
/// The collector against a real provider over canned HTTP and a real directory, so what lands on
/// disk is what a triage would actually leave there.
/// </summary>
[NotInParallel(nameof(AppPaths))]
public class ArtifactCollectorTests :
    IDisposable
{
    const string failingBuild = "gh/Verify/test.yml/feature/inline";
    const string runs = "https://api.github.com/repos/VerifyTests/Verify/actions/runs/77";
    const string artifacts = "https://api.github.com/repos/VerifyTests/Verify/actions/artifacts";

    readonly string original = AppPaths.Directory;
    readonly string directory = Path.Combine(Path.GetTempPath(), $"BuildMonitorCollect_{Guid.NewGuid():N}");

    public ArtifactCollectorTests() =>
        AppPaths.Directory = directory;

    /// <summary>
    /// The failing row carrying what a real poll would have left on it, which is what the provider
    /// composes its URLs from.
    /// </summary>
    static (Poller Poller, Build Build) Create(FakeHttpHandler http)
    {
        var host = new SessionHost(Fixtures.WithBuilds());
        host.Mutate(_ => _ with
        {
            Builds = _.Builds.Replace(
                _.Builds.Single(_ => _.Key == failingBuild),
                _.Builds.Single(_ => _.Key == failingBuild) with
                {
                    ProviderRef = "VerifyTests/Verify|77|failure"
                })
        });
        var poller = new Poller(host, new MemorySecretStore(), new(), http);
        return (poller, host.State.Builds.Single(_ => _.Key == failingBuild));
    }

    /// <summary>
    /// One failed job with a log, which is what every case here builds on.
    /// </summary>
    static FakeHttpHandler Handler(string log = "error CS1002: ; expected\n") =>
        new FakeHttpHandler()
            .Get($"{runs}/jobs?filter=latest&per_page=100&page=1", """{"total_count":1,"jobs":[{"id":72,"name":"build","conclusion":"failure"}]}""")
            .Get("https://api.github.com/repos/VerifyTests/Verify/actions/jobs/72/logs", log);

    static FakeHttpHandler WithArtifacts(FakeHttpHandler handler, string listing) =>
        handler.Get($"{runs}/artifacts?per_page=100&page=1", listing);

    [Test]
    public async Task TheLogAndTheArtifactsLandTogether()
    {
        var handler = WithArtifacts(
            Handler(),
            """{"total_count":1,"artifacts":[{"id":81,"name":"test-results","size_in_bytes":4,"expired":false}]}""");
        handler.MapBytes("GET", $"{artifacts}/81/zip", [80, 75, 3, 4]);
        var (poller, build) = Create(handler);
        var files = await ArtifactCollector.Collect(new(), poller, build, Cancel.None);
        await Assert.That(files.Files).IsEquivalentTo(["log.txt", "test-results.zip"]);
        await Assert.That(files.Skipped).IsEmpty();
        await Assert.That(files.Unsupported).IsNull();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(files.Directory, "log.txt"))).IsEqualTo("==> build <==\nerror CS1002: ; expected");
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(files.Directory, "test-results.zip"))).IsEquivalentTo(new byte[] {80, 75, 3, 4});
    }

    [Test]
    public async Task ARunThatPublishedNothingGetsItsLogAlone()
    {
        var (poller, build) = Create(WithArtifacts(Handler(), """{"total_count":0,"artifacts":[]}"""));
        var files = await ArtifactCollector.Collect(new(), poller, build, Cancel.None);
        await Assert.That(files.Files).IsEquivalentTo(["log.txt"]);
        await Assert.That(files.Unsupported).IsNull();
    }

    /// <summary>
    /// A run that failed before it started a job has no log, and if it published nothing either
    /// there is nothing to write. No directory is left for the sweep to find later.
    /// </summary>
    [Test]
    public async Task ARunWithNeitherWritesNoDirectory()
    {
        var handler = WithArtifacts(
            new FakeHttpHandler().Get($"{runs}/jobs?filter=latest&per_page=100&page=1", """{"total_count":0,"jobs":[]}"""),
            """{"total_count":0,"artifacts":[]}""");
        var (poller, build) = Create(handler);
        var files = await ArtifactCollector.Collect(new(), poller, build, Cancel.None);
        await Assert.That(files.Directory).IsEmpty();
        await Assert.That(files.Files).IsEmpty();
        await Assert.That(Directory.Exists(AppPaths.Artifacts)).IsFalse();
    }

    /// <summary>
    /// Two jobs archiving a results file each call it the same thing, and the second would
    /// otherwise replace the first with nothing saying so.
    /// </summary>
    [Test]
    public async Task TwoArtifactsOfOneNameBothSurvive()
    {
        var handler = WithArtifacts(
            Handler(),
            """
            {"total_count":2,"artifacts":[
              {"id":81,"name":"results","size_in_bytes":3,"expired":false},
              {"id":82,"name":"results","size_in_bytes":4,"expired":false}
            ]}
            """);
        handler.MapBytes("GET", $"{artifacts}/81/zip", [1, 2, 3]);
        handler.MapBytes("GET", $"{artifacts}/82/zip", [4, 5, 6, 7]);
        var (poller, build) = Create(handler);
        var files = await ArtifactCollector.Collect(new(), poller, build, Cancel.None);
        await Assert.That(files.Files).IsEquivalentTo(["log.txt", "results.zip", "results-2.zip"]);
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(files.Directory, "results.zip"))).IsEquivalentTo(new byte[] {1, 2, 3});
        await Assert.That(await File.ReadAllBytesAsync(Path.Combine(files.Directory, "results-2.zip"))).IsEquivalentTo(new byte[] {4, 5, 6, 7});
    }

    /// <summary>
    /// log.txt is claimed before any artifact is named, so a run that published a file that
    /// sanitises to that name cannot quietly replace the build's own log with it.
    /// </summary>
    [Test]
    public async Task TheBuildLogKeepsItsNameWhateverTheRunPublished()
    {
        var handler = WithArtifacts(
            Handler(),
            """{"total_count":1,"artifacts":[{"id":81,"name":"log.txt","size_in_bytes":3,"expired":false}]}""");
        handler.MapBytes("GET", $"{artifacts}/81/zip", [1, 2, 3]);
        var (poller, build) = Create(handler);
        var files = await ArtifactCollector.Collect(new(), poller, build, Cancel.None);
        // GitHub serves every artifact as a zip, so its name gains that extension and the two
        // cannot land on one name in the first place.
        await Assert.That(files.Files).IsEquivalentTo(["log.txt", "log.txt.zip"]);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(files.Directory, "log.txt"))).StartsWith("==> build <==");
    }

    /// <summary>
    /// One artifact that will not come is worth saying, and the ones that did are still worth
    /// having, so the download is reported rather than failing the whole bundle.
    /// </summary>
    [Test]
    public async Task AnArtifactThatFailsToDownloadIsReportedAndTheRestKept()
    {
        var handler = WithArtifacts(
            Handler(),
            """
            {"total_count":2,"artifacts":[
              {"id":81,"name":"test-results","size_in_bytes":4,"expired":false},
              {"id":82,"name":"gone","size_in_bytes":4,"expired":false}
            ]}
            """);
        handler.MapBytes("GET", $"{artifacts}/81/zip", [80, 75, 3, 4]);
        var (poller, build) = Create(handler);
        var files = await ArtifactCollector.Collect(new(), poller, build, Cancel.None);
        await Assert.That(files.Files).IsEquivalentTo(["log.txt", "test-results.zip"]);
        await Assert.That(files.Skipped.Single().Name).IsEqualTo("gone.zip");
        await Assert.That(files.Skipped.Single().Reason).StartsWith("the download failed: 404");
        await Assert.That(File.Exists(Path.Combine(files.Directory, "gone.zip"))).IsFalse();
    }

    /// <summary>
    /// An expired artifact never reaches a download, and keeps the service's own word for why.
    /// </summary>
    [Test]
    public async Task AnExpiredArtifactIsReportedWithoutBeingAskedFor()
    {
        var handler = WithArtifacts(
            Handler(),
            """{"total_count":1,"artifacts":[{"id":81,"name":"old","size_in_bytes":4,"expired":true}]}""");
        var (poller, build) = Create(handler);
        var files = await ArtifactCollector.Collect(new(), poller, build, Cancel.None);
        await Assert.That(files.Files).IsEquivalentTo(["log.txt"]);
        await Assert.That(files.Skipped.Single().Reason).IsEqualTo("expired");
        await Assert.That(handler.Requests.Any(_ => _.Contains("/81/zip"))).IsFalse();
    }

    /// <summary>
    /// A listing that fails is not a failed collect either: the log is still worth handing over.
    /// </summary>
    [Test]
    public async Task ALogThatCannotBeFetchedStillLeavesTheArtifacts()
    {
        var handler = WithArtifacts(
            new FakeHttpHandler().Map("GET", $"{runs}/jobs?filter=latest&per_page=100&page=1", "nope", HttpStatusCode.InternalServerError),
            """{"total_count":1,"artifacts":[{"id":81,"name":"test-results","size_in_bytes":4,"expired":false}]}""");
        handler.MapBytes("GET", $"{artifacts}/81/zip", [80, 75, 3, 4]);
        var (poller, build) = Create(handler);
        var files = await ArtifactCollector.Collect(new(), poller, build, Cancel.None);
        await Assert.That(files.Files).IsEquivalentTo(["test-results.zip"]);
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
