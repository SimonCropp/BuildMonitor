public class LockWatcherTests
{
    sealed class StubLock(Func<bool?> answer) : ISessionLock
    {
        public ValueTask<bool?> IsLocked(Cancel cancel) =>
            ValueTask.FromResult(answer());
    }

    sealed class ThrowingLock : ISessionLock
    {
        public ValueTask<bool?> IsLocked(Cancel cancel) =>
            throw new InvalidOperationException("No session");
    }

    static (SessionHost Host, LockWatcher Watcher, Func<int> Refreshes) Watch(ISessionLock? sessionLock = null)
    {
        var host = new SessionHost(Fixtures.WithBuilds());
        var refreshes = 0;
        var watcher = new LockWatcher(host, sessionLock ?? new StubLock(() => null), () => refreshes++, () => Fixtures.Now);
        return (host, watcher, () => refreshes);
    }

    [Test]
    public async Task LockingSetsTheStateWithoutARefresh()
    {
        var (host, watcher, refreshes) = Watch();
        watcher.Apply(true, Fixtures.Now);
        await Assert.That(host.State.Locked).IsNotNull();
        await Assert.That(refreshes()).IsEqualTo(0);
    }

    [Test]
    public async Task UnlockingClearsTheStateAndRefreshes()
    {
        var (host, watcher, refreshes) = Watch();
        watcher.Apply(true, Fixtures.Now);
        watcher.Apply(false, Fixtures.Now + LockWatcher.Every);
        await Assert.That(host.State.Locked).IsNull();
        await Assert.That(refreshes()).IsEqualTo(1);
    }

    [Test]
    public async Task StayingUnlockedDoesNothing()
    {
        var (host, watcher, refreshes) = Watch();
        var before = host.State;
        watcher.Apply(false, Fixtures.Now);
        watcher.Apply(false, Fixtures.Now + LockWatcher.Every);
        await Assert.That(host.State).IsSameReferenceAs(before);
        await Assert.That(refreshes()).IsEqualTo(0);
    }

    [Test]
    public async Task NoAnswerLeavesTheLockAsItIs()
    {
        var (host, watcher, refreshes) = Watch();
        watcher.Apply(true, Fixtures.Now);
        watcher.Apply(null, Fixtures.Now + LockWatcher.Every);
        await Assert.That(host.State.Locked).IsNotNull();
        await Assert.That(refreshes()).IsEqualTo(0);
    }

    [Test]
    public async Task ALongGapWhileUnlockedIsASleepAndRefreshes()
    {
        var (_, watcher, refreshes) = Watch();
        watcher.Apply(false, Fixtures.Now);
        watcher.Apply(false, Fixtures.Now + TimeSpan.FromMinutes(30));
        await Assert.That(refreshes()).IsEqualTo(1);
    }

    [Test]
    public async Task ALongGapWhileLockedWaitsForTheUnlock()
    {
        var (_, watcher, refreshes) = Watch();
        watcher.Apply(true, Fixtures.Now);
        watcher.Apply(true, Fixtures.Now + TimeSpan.FromMinutes(30));
        await Assert.That(refreshes()).IsEqualTo(0);
        watcher.Apply(false, Fixtures.Now + TimeSpan.FromMinutes(31));
        await Assert.That(refreshes()).IsEqualTo(1);
    }

    [Test]
    public async Task AskingGoesThroughTheLock()
    {
        var locked = true;
        var (host, watcher, _) = Watch(new StubLock(() => locked));
        await watcher.Look(Cancel.None);
        await Assert.That(host.State.Locked).IsNotNull();
        locked = false;
        await watcher.Look(Cancel.None);
        await Assert.That(host.State.Locked).IsNull();
    }

    /// <summary>
    /// What it answers depends on where the tests run: a CI runner's session may not say either
    /// way. Only that asking does not throw, which a wrong signature or offset would.
    /// </summary>
    [Test]
    public async Task ThePlatformsLockCanBeAsked()
    {
        var sessionLock = SessionLocks.ForPlatform();
        if (OperatingSystem.IsWindows())
        {
            await Assert.That(sessionLock).IsTypeOf<WindowsSessionLock>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            await Assert.That(sessionLock).IsTypeOf<MacSessionLock>();
        }
        else
        {
            await Assert.That(sessionLock).IsNull();
        }

        if (sessionLock is not null)
        {
            await sessionLock.IsLocked(Cancel.None);
        }
    }

    [Test]
    public async Task ALockThatThrowsIsNoAnswer()
    {
        var (host, watcher, refreshes) = Watch(new ThrowingLock());
        await watcher.Look(Cancel.None);
        await watcher.Look(Cancel.None);
        await Assert.That(host.State.Locked).IsNull();
        await Assert.That(refreshes()).IsEqualTo(0);
    }
}
