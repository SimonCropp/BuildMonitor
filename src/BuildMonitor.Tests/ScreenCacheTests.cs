public class ScreenCacheTests
{
    static readonly DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SameStateWithinTickReusesScreen()
    {
        var cache = new ScreenCache();
        var state = Fixtures.Connected();
        var first = cache.Get(state, now, out var firstRebuilt);
        var second = cache.Get(state, now.AddMilliseconds(100), out var secondRebuilt);

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
        var state = Fixtures.Connected();
        var first = cache.Get(state, now, out _);
        var second = cache.Get(state, now + ScreenCache.ClockTick, out var rebuilt);

        await Assert.That(rebuilt).IsTrue();
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }
}
