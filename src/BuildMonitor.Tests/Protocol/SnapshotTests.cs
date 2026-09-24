public class SnapshotTests
{
    [Test]
    public async Task PipelinesCountTheirRuns()
    {
        var pipelines = Snapshot.Pipelines(Fixtures.WatchOnly());
        await Assert.That(pipelines.Select(_ => $"{_.Key} {_.Runs}"))
            .IsEquivalentTo(["gh/DiffEngine/test.yml 1", "gh/DiffEngine/docs.yml 1", "gh/Verify/test.yml 2"]);
    }

    [Test]
    [Arguments("gh/Verify/test.yml/feature/inline")]
    [Arguments("gh/Verify/test.yml/main")]
    [Arguments("gh/Verify/test.yml")]
    public async Task RunsResolveABuildKeyOrAPipelineKey(string key)
    {
        var runs = Snapshot.Runs(Fixtures.WithBuilds(), key, Fixtures.Now);
        await Assert.That(runs!.Select(_ => $"{_.Key} #{_.Run}"))
            .IsEquivalentTo(["gh/Verify/test.yml/feature/inline #77", "gh/Verify/test.yml/main #76"]);
    }

    const string deferredKey = "gh/Verify/test.yml/feature/inline";

    static SessionState WithDeferral(DateTimeOffset until)
    {
        var state = Fixtures.WithBuilds();
        return state with
        {
            Settings = state.Settings with
            {
                Deferrals = [new(deferredKey, "test.yml failure on feature/inline", until)]
            }
        };
    }

    /// <summary>
    /// A deferred failure leaves the list and the failing count as it leaves the window, and is
    /// named apart, with when it comes back, rather than vanishing.
    /// </summary>
    [Test]
    public async Task ADeferredFailureIsListedApartWithItsEnd()
    {
        var until = Fixtures.Now.AddDays(3);
        var state = WithDeferral(until);

        await Assert.That(Snapshot.Builds(state, Fixtures.Now).Any(_ => _.Key == deferredKey)).IsFalse();
        var deferred = Snapshot.Deferred(state, Fixtures.Now).Single();
        await Assert.That(deferred.Key).IsEqualTo(deferredKey);
        await Assert.That(deferred.DeferredUntil).IsEqualTo(until);
        await Assert.That(Snapshot.Summary(state, Fixtures.Now).Deferred).IsEqualTo(1);
        await Assert.That(Snapshot.Find(state, deferredKey, Fixtures.Now)!.DeferredUntil).IsEqualTo(until);
    }

    [Test]
    public async Task ADueDeferralIsNotHeld()
    {
        var state = WithDeferral(Fixtures.Now.AddMinutes(-1));
        await Assert.That(Snapshot.Deferred(state, Fixtures.Now)).IsEmpty();
        await Assert.That(Snapshot.Summary(state, Fixtures.Now).Deferred).IsEqualTo(0);
    }

    [Test]
    public async Task NothingDeferredMarksNothing()
    {
        var state = Fixtures.WithBuilds();
        await Assert.That(Snapshot.Deferred(state, Fixtures.Now)).IsEmpty();
        await Assert.That(Snapshot.Builds(state, Fixtures.Now).All(_ => _.DeferredUntil is null)).IsTrue();
    }

    [Test]
    [Arguments("gh/Verify/test.yml/")]
    [Arguments("gh/Verify")]
    [Arguments("jenkins/Verify/test.yml")]
    public async Task RunsOfNoPipelineAreNull(string key) =>
        await Assert.That(Snapshot.Runs(Fixtures.WithBuilds(), key, Fixtures.Now)).IsNull();
}
