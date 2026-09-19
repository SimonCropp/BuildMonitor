public class NotificationTests
{
    [Test]
    public async Task ARunningBuildThatFailsIsAnnounced()
    {
        var state = Fixtures.WithBuilds();
        var builds = Fixtures.GitHubBuilds();
        var failed = builds.Select(_ => _.RunNumber == "1234" ? _ with { Status = BuildStatus.Failed, Finished = Fixtures.Now } : _).ToImmutableArray();
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, state.Connection(Fixtures.GitHub.Id)!.Pipelines, failed, Fixtures.Now);
        await Assert.That(next.Notification).IsEqualTo(new("test.yml failed", "VerifyTests/DiffEngine main #1234"));
        await Verify(Fixtures.Render(next));
    }

    [Test]
    public async Task ADependabotBranchLeavesOutTheEcosystem() =>
        await Assert.That(Fixtures.WithDependabotFailure().Notification)
            .IsEqualTo(new("build.yml failed", "VerifyTests/Reports 🤖 Polyfill-9.1.0 #9"));

    [Test]
    public async Task AnAlreadyFailedBuildIsNotAnnouncedAgain()
    {
        var state = Fixtures.WithBuilds();
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, state.Connection(Fixtures.GitHub.Id)!.Pipelines, Fixtures.GitHubBuilds(), Fixtures.Now);
        await Assert.That(next.Notification).IsNull();
    }

    [Test]
    public async Task TheFirstPollIsSilent()
    {
        var state = Fixtures.Connected();
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], Fixtures.GitHubBuilds(), Fixtures.Now);
        await Assert.That(next.Notification).IsNull();
    }

    [Test]
    public async Task SeveralFailuresAreSummarised()
    {
        var state = Fixtures.WithBuilds();
        var failed = Fixtures.GitHubBuilds().Select(_ => _ with { Status = BuildStatus.Failed }).ToImmutableArray();
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], failed, Fixtures.Now);
        await Assert.That(next.Notification!.Title).IsEqualTo("3 builds failed");
        await Assert.That(next.Notification.Message).IsEqualTo("test.yml, docs.yml");
    }

    [Test]
    public async Task CanBeSwitchedOff()
    {
        var state = Fixtures.WithBuilds();
        state = MonitorSession.ApplySettings(state, state.Settings with { NotifyOnFailure = false });
        var failed = Fixtures.GitHubBuilds().Select(_ => _ with { Status = BuildStatus.Failed }).ToImmutableArray();
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], failed, Fixtures.Now);
        await Assert.That(next.Notification).IsNull();
    }

    [Test]
    public async Task ClearedAfterShowing()
    {
        var state = Fixtures.WithBuilds() with { Notification = new("x", "y") };
        await Assert.That(MonitorSession.ClearNotification(state).Notification).IsNull();
    }
}
