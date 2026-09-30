public class LockTests
{
    static ImmutableArray<Build> WithFailure() =>
    [
        ..Fixtures.GitHubBuilds().Select(_ =>
        {
            if (_.RunNumber == "1234")
            {
                return _ with
                {
                    Status = BuildStatus.Failed,
                    Finished = Fixtures.Now
                };
            }

            return _;
        })
    ];

    static SessionState Poll(SessionState state, ImmutableArray<Build> builds) =>
        MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, state.Connection(Fixtures.GitHub.Id)!.Pipelines, builds, Fixtures.Now);

    [Test]
    public async Task AFailureWhileLockedIsNotAnnounced()
    {
        var locked = MonitorSession.Lock(Fixtures.WithBuilds());
        await Assert.That(Poll(locked, WithFailure()).Notification).IsNull();
    }

    [Test]
    public async Task UnlockingAnnouncesWhatFailedWhileLocked()
    {
        var locked = Poll(MonitorSession.Lock(Fixtures.WithBuilds()), WithFailure());
        var unlocked = MonitorSession.Unlock(locked);
        await Assert.That(unlocked.Locked).IsNull();
        await Assert.That(unlocked.Notification).IsEqualTo(new("test.yml failed", "VerifyTests/DiffEngine @main #1234", WithFailure().Single(_ => _.RunNumber == "1234").Key));
    }

    [Test]
    public async Task AFailureThatPassedAgainWhileLockedIsNotAnnounced()
    {
        var passed = WithFailure().Select(_ =>
        {
            if (_.RunNumber == "1234")
            {
                return _ with
                {
                    RunNumber = "1235",
                    Status = BuildStatus.Succeeded,
                    Started = Fixtures.Now,
                    Finished = Fixtures.Now + TimeSpan.FromMinutes(1)
                };
            }

            return _;
        });
        var failed = Poll(MonitorSession.Lock(Fixtures.WithBuilds()), WithFailure());
        var locked = Poll(failed, [..WithFailure(), ..passed.Where(_ => _.RunNumber == "1235")]);
        await Assert.That(MonitorSession.Unlock(locked).Notification).IsNull();
    }

    [Test]
    public async Task LockingAgainKeepsWhatTheFirstLockSaw()
    {
        var locked = Poll(MonitorSession.Lock(Fixtures.WithBuilds()), WithFailure());
        var again = MonitorSession.Lock(locked);
        await Assert.That(MonitorSession.Unlock(again).Notification).IsNotNull();
    }

    [Test]
    public async Task UnlockingWhenNotLockedChangesNothing()
    {
        var state = Fixtures.WithBuilds();
        await Assert.That(MonitorSession.Unlock(state)).IsSameReferenceAs(state);
    }

    [Test]
    public async Task UnlockingAnnouncesNothingWhenSwitchedOff()
    {
        var state = Fixtures.WithBuilds();
        state = MonitorSession.ApplySettings(state, state.Settings with
        {
            NotifyOnFailure = false
        });
        var locked = Poll(MonitorSession.Lock(state), WithFailure());
        await Assert.That(MonitorSession.Unlock(locked).Notification).IsNull();
    }

    [Test]
    public async Task LockingBeforeTheFirstPollAnnouncesNothing()
    {
        var locked = MonitorSession.Lock(Fixtures.Connected());
        var polled = MonitorSession.ApplyPoll(locked, Fixtures.GitHub.Id, [], WithFailure(), Fixtures.Now);
        await Assert.That(MonitorSession.Unlock(polled).Notification).IsNull();
    }
}
