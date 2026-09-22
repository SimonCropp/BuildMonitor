public class ApplyFetchTests
{
    static readonly ImmutableArray<Pipeline> pipelines =
    [
        new("DiffEngine/test.yml", "test.yml", "VerifyTests/DiffEngine", "VerifyTests/DiffEngine", "https://github.com/VerifyTests/DiffEngine"),
        new("DiffEngine/docs.yml", "docs.yml", "VerifyTests/DiffEngine", "VerifyTests/DiffEngine", "https://github.com/VerifyTests/DiffEngine"),
        new("Verify/test.yml", "test.yml", "VerifyTests/Verify", "VerifyTests/Verify", "https://github.com/VerifyTests/Verify")
    ];

    static FetchOutcome Outcome(ImmutableArray<Pipeline> discovered, IEnumerable<string> fetched, IEnumerable<Build> builds, IEnumerable<string>? firstFetch = null) =>
        new(discovered, [.. fetched], [.. firstFetch ?? []], [.. builds], ConnectionHealth.Ok, null, null);

    static string Runs(SessionState state) =>
        string.Join(
            ',',
            state.Builds
                .Where(_ => _.ConnectionId == Fixtures.GitHub.Id)
                .Select(_ => $"{_.PipelineId}#{_.RunNumber}")
                .Order(StringComparer.Ordinal));

    [Test]
    public async Task OnlyTheFetchedPipelinesAreReplaced()
    {
        var rerun = Fixtures.Build(Fixtures.GitHub.Id, "Verify/test.yml", "test.yml", "VerifyTests/Verify", "main", "78", BuildStatus.Running, started: Fixtures.Now);
        var next = MonitorSession.ApplyFetch(Fixtures.WithBuilds(), Fixtures.GitHub.Id, Outcome(pipelines, ["Verify/test.yml"], [rerun]), Fixtures.Now);
        await Assert.That(Runs(next)).IsEqualTo("DiffEngine/docs.yml#300,DiffEngine/test.yml#1234,Verify/test.yml#78");
    }

    [Test]
    public async Task TheSelectionStaysOnItsRow()
    {
        // The DiffEngine run above the failed Verify run finishing sorts the failure up a row.
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.SelectRow(builds, Fixtures.RowOf(builds, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline"));
        var finished = Fixtures.Build(Fixtures.GitHub.Id, "DiffEngine/test.yml", "test.yml", "VerifyTests/DiffEngine", "main", "1234", BuildStatus.Succeeded, started: Fixtures.Now - TimeSpan.FromMinutes(3), finished: Fixtures.Now);
        var next = MonitorSession.ApplyFetch(state, Fixtures.GitHub.Id, Outcome(pipelines, ["DiffEngine/test.yml"], [finished]), Fixtures.Now);
        await Assert.That(MonitorSession.SelectedBuild(next)?.Key).IsEqualTo("gh/Verify/test.yml/feature/inline");
    }

    [Test]
    public async Task BuildsOfPipelinesNoLongerDiscoveredGo()
    {
        var next = MonitorSession.ApplyFetch(Fixtures.WithBuilds(), Fixtures.GitHub.Id, Outcome([pipelines[0]], [], []), Fixtures.Now);
        await Assert.That(Runs(next)).IsEqualTo("DiffEngine/test.yml#1234");
    }

    [Test]
    public async Task OtherConnectionsAreUntouched()
    {
        var state = Fixtures.WithBuilds();
        var next = MonitorSession.ApplyFetch(state, Fixtures.GitHub.Id, Outcome(pipelines, [], []), Fixtures.Now);
        await Assert.That(next.Builds.Count(_ => _.ConnectionId != Fixtures.GitHub.Id))
            .IsEqualTo(state.Builds.Count(_ => _.ConnectionId != Fixtures.GitHub.Id));
    }

    [Test]
    public async Task LastPolledOnlyMovesWhenSomethingWasFetched()
    {
        var state = Fixtures.WithBuilds();
        var before = state.Connection(Fixtures.GitHub.Id)!.LastPolled;
        var idle = MonitorSession.ApplyFetch(state, Fixtures.GitHub.Id, Outcome(pipelines, [], []), Fixtures.Now);
        await Assert.That(idle.Connection(Fixtures.GitHub.Id)!.LastPolled).IsEqualTo(before);
        var fetched = MonitorSession.ApplyFetch(state, Fixtures.GitHub.Id, Outcome(pipelines, ["DiffEngine/docs.yml"], []), Fixtures.Now);
        await Assert.That(fetched.Connection(Fixtures.GitHub.Id)!.LastPolled).IsEqualTo(Fixtures.Now);
    }

    [Test]
    public async Task AFailureOfAKnownPipelineIsNews()
    {
        var failed = Fixtures.Build(Fixtures.GitHub.Id, "Verify/test.yml", "test.yml", "VerifyTests/Verify", "main", "78", BuildStatus.Failed, started: Fixtures.Now, finished: Fixtures.Now);
        var next = MonitorSession.ApplyFetch(Fixtures.WithBuilds(), Fixtures.GitHub.Id, Outcome(pipelines, ["Verify/test.yml"], [failed]), Fixtures.Now);
        await Assert.That(next.Notification).IsEqualTo(new("test.yml failed", "VerifyTests/Verify main #78", failed.Key));
    }

    [Test]
    [Arguments(BuildStatus.Running, 40, null, true)]
    [Arguments(BuildStatus.Queued, 40, null, true)]
    [Arguments(BuildStatus.Succeeded, 40, 10, true)]
    [Arguments(BuildStatus.Succeeded, 40, 35, false)]
    public async Task ABuildFromBeforeTheHistory(BuildStatus status, int startedDaysAgo, int? finishedDaysAgo, bool shown)
    {
        var old = Fixtures.Build(
            Fixtures.GitHub.Id,
            "Verify/test.yml",
            "test.yml",
            "VerifyTests/Verify",
            "main",
            "78",
            status,
            started: Fixtures.Now - TimeSpan.FromDays(startedDaysAgo),
            finished: finishedDaysAgo is { } days ? Fixtures.Now - TimeSpan.FromDays(days) : null);
        var next = MonitorSession.ApplyFetch(Fixtures.WithBuilds(), Fixtures.GitHub.Id, Outcome(pipelines, ["Verify/test.yml"], [old]), Fixtures.Now);
        await Assert.That(Runs(next).Contains("Verify/test.yml#78")).IsEqualTo(shown);
    }

    [Test]
    public async Task ARunningBuildFromBeforeTheHistoryStaysWhenItsPipelineIsNotDue()
    {
        var old = Fixtures.Build(Fixtures.GitHub.Id, "Verify/test.yml", "test.yml", "VerifyTests/Verify", "main", "78", BuildStatus.Running, started: Fixtures.Now - TimeSpan.FromDays(40));
        var running = MonitorSession.ApplyFetch(Fixtures.WithBuilds(), Fixtures.GitHub.Id, Outcome(pipelines, ["Verify/test.yml"], [old]), Fixtures.Now);
        var next = MonitorSession.ApplyFetch(running, Fixtures.GitHub.Id, Outcome(pipelines, ["DiffEngine/test.yml"], []), Fixtures.Now + TimeSpan.FromDays(1));
        await Assert.That(Runs(next)).Contains("Verify/test.yml#78");
    }

    [Test]
    public async Task AFirstFetchIsNotNews()
    {
        var discovered = pipelines.Add(new("New/ci.yml", "ci.yml", "VerifyTests/New", "VerifyTests/New", "https://github.com/VerifyTests/New"));
        var failed = Fixtures.Build(Fixtures.GitHub.Id, "New/ci.yml", "ci.yml", "VerifyTests/New", "main", "1", BuildStatus.Failed, started: Fixtures.Now, finished: Fixtures.Now);
        var next = MonitorSession.ApplyFetch(Fixtures.WithBuilds(), Fixtures.GitHub.Id, Outcome(discovered, ["New/ci.yml"], [failed], ["New/ci.yml"]), Fixtures.Now);
        await Assert.That(next.Notification).IsNull();
    }

    static bool Offers(SessionState state, string connectionId) =>
        state.Builds.Any(_ => _.ConnectionId == connectionId && (_.CanRetry || _.CanCancel));

    [Test]
    public async Task AConnectionThatCanOnlyWatchOffersNoRetryOrCancel()
    {
        // The kept builds too, or their rows would offer what the connection may not do until their
        // group came due.
        var state = Fixtures.WithBuilds();
        var running = Fixtures.Build(Fixtures.GitHub.Id, "Verify/test.yml", "test.yml", "VerifyTests/Verify", "main", "78", BuildStatus.Running, started: Fixtures.Now);
        var next = MonitorSession.ApplyFetch(state, Fixtures.GitHub.Id, Outcome(pipelines, ["Verify/test.yml"], [running])
            with
            {
                Access = BuildAccess.Watch
            }, Fixtures.Now);
        await Assert.That(Offers(state, Fixtures.GitHub.Id)).IsTrue();
        await Assert.That(Offers(next, Fixtures.GitHub.Id)).IsFalse();
        await Assert.That(Offers(next, Fixtures.Jenkins.Id)).IsTrue();
        await Assert.That(next.Connection(Fixtures.GitHub.Id)!.Access).IsEqualTo(BuildAccess.Watch);
    }

    [Test]
    public async Task AConnectionThatMayChangeBuildsKeepsWhatItsProviderOffers()
    {
        var running = Fixtures.Build(Fixtures.GitHub.Id, "Verify/test.yml", "test.yml", "VerifyTests/Verify", "main", "78", BuildStatus.Running, started: Fixtures.Now);
        var next = MonitorSession.ApplyFetch(Fixtures.WatchOnly(), Fixtures.GitHub.Id, Outcome(pipelines, ["Verify/test.yml"], [running]) with
        {
            Access = BuildAccess.Change
        }, Fixtures.Now);
        await Assert.That(next.Builds.Single(_ => _.RunNumber == "78").CanCancel).IsTrue();
        await Assert.That(next.Connection(Fixtures.GitHub.Id)!.Access).IsEqualTo(BuildAccess.Change);
    }
}
