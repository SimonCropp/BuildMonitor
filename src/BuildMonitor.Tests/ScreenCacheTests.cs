public class ScreenCacheTests
{
    static readonly DateTimeOffset now = Fixtures.Now;

    [Test]
    public async Task SameStateWithinTickReusesScreen()
    {
        var cache = new ScreenCache();
        var state = Fixtures.WithBuilds();
        var first = cache.Get(state, now, out var firstRebuilt);
        var second = cache.Get(state, now + ScreenCache.ClockTick - TimeSpan.FromMilliseconds(1), out var secondRebuilt);

        await Assert.That(firstRebuilt).IsTrue();
        await Assert.That(secondRebuilt).IsFalse();
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task ChangedStateRebuilds()
    {
        var cache = new ScreenCache();
        var state = Fixtures.Connected();
        var first = cache.Get(state, now, out _);
        var second = cache.Get(MonitorSession.SetStatus(state, "changed"), now, out var rebuilt);

        await Assert.That(rebuilt).IsTrue();
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        await Assert.That(second.Status).IsEqualTo("changed");
    }

    [Test]
    public async Task NextTickRebuilds()
    {
        var cache = new ScreenCache();
        var state = Fixtures.WithBuilds();
        var first = cache.Get(state, now, out _);
        var second = cache.Get(state, now + ScreenCache.ClockTick, out var rebuilt);

        await Assert.That(rebuilt).IsTrue();
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }

    [Test]
    public async Task TheTickIsOnWholeSeconds()
    {
        // Built a millisecond before a second turns over, the times on screen are a second behind
        // one millisecond later.
        var cache = new ScreenCache();
        var state = Fixtures.WithBuilds();
        cache.Get(state, now - TimeSpan.FromMilliseconds(1), out _);
        cache.Get(state, now, out var rebuilt);

        await Assert.That(rebuilt).IsTrue();
    }

    [Test]
    public async Task LoadingTicksFaster()
    {
        // No connection has polled, so the page shows the spinner, which on Windows turns only as
        // the canvas repaints.
        var cache = new ScreenCache();
        var state = Fixtures.Connected();
        var first = cache.Get(state, now, out _);
        cache.Get(state, now + ScreenCache.LoadingTick, out var rebuilt);

        await Assert.That(first.Builds!.Loading).IsTrue();
        await Assert.That(rebuilt).IsTrue();
    }

    [Test]
    public async Task HiddenIgnoresTheClock()
    {
        var cache = new ScreenCache();
        var state = Fixtures.Hidden();
        var first = cache.Get(state, now, out _);
        var second = cache.Get(state, now + TimeSpan.FromMinutes(5), out var rebuilt);

        await Assert.That(rebuilt).IsFalse();
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task ShowingRebuildsWithTheClock()
    {
        var cache = new ScreenCache();
        var hidden = Fixtures.Hidden();
        var first = cache.Get(hidden, now, out _);
        var shown = cache.Get(MonitorSession.Show(hidden), now + TimeSpan.FromMinutes(5), out var rebuilt);

        await Assert.That(rebuilt).IsTrue();
        await Assert.That(first.Status).IsEqualTo("Polled 5s ago");
        await Assert.That(shown.Status).IsEqualTo("Polled 5m ago");
    }

    [Test]
    public async Task UntilTickIsToTheNextWholeSecond()
    {
        var cache = new ScreenCache();
        cache.Get(Fixtures.WithBuilds(), now + TimeSpan.FromMilliseconds(300), out _);

        await Assert.That(cache.UntilTick(now + TimeSpan.FromMilliseconds(400))).IsEqualTo(TimeSpan.FromMilliseconds(600));
        // Past it, as when the loop wakes late, the tick is due at once.
        await Assert.That(cache.UntilTick(now + TimeSpan.FromMilliseconds(1100))).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task UntilTickKeepsUpWithTheSpinner()
    {
        var cache = new ScreenCache();
        cache.Get(Fixtures.Connected(), now + TimeSpan.FromMilliseconds(100), out _);

        await Assert.That(cache.UntilTick(now + TimeSpan.FromMilliseconds(100))).IsEqualTo(TimeSpan.FromMilliseconds(150));
    }

    [Test]
    public async Task NoTickIsComingWhileHidden()
    {
        var cache = new ScreenCache();
        await Assert.That(cache.UntilTick(now)).IsNull();
        cache.Get(Fixtures.Hidden(), now, out _);
        await Assert.That(cache.UntilTick(now)).IsNull();
    }

    [Test]
    public async Task TheLoopIdlesUntilTheTick()
    {
        var cache = new ScreenCache();
        var state = Fixtures.WithBuilds();
        cache.Get(state, now, out _);

        await Assert.That(MonitorProgram.Idle(cache, state, now + TimeSpan.FromMilliseconds(250))).IsEqualTo(TimeSpan.FromMilliseconds(750));
    }

    [Test]
    public async Task HiddenTheLoopIdlesUntilWoken()
    {
        var cache = new ScreenCache();
        var state = Fixtures.Hidden();
        cache.Get(state, now, out _);

        await Assert.That(MonitorProgram.Idle(cache, state, now)).IsEqualTo(MonitorProgram.LongestIdle);
    }

    /// <summary>
    /// The pump offers a copy the desktop refused again on the next frame, and a refusal changes no
    /// state to wake the loop with.
    /// </summary>
    [Test]
    public async Task ACopyStillToOfferIsTriedAFrameLater()
    {
        var cache = new ScreenCache();
        var state = Fixtures.Hidden() with
        {
            Clipboard = new("text")
        };
        cache.Get(state, now, out _);

        await Assert.That(MonitorProgram.Idle(cache, state, now)).IsEqualTo(MonitorProgram.Frame);
    }
}
