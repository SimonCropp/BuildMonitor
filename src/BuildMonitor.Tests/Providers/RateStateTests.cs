public class RateStateTests
{
    static readonly DateTimeOffset now = Fixtures.Now;
    static readonly DateTimeOffset reset = Fixtures.Now.AddMinutes(40);

    static RateObservation Count(double remaining, DateTimeOffset? resetAt = null) =>
        new(5000, remaining, resetAt ?? reset, null, null, null);

    [Test]
    public async Task TheLowestRemainingWinsWithinAWindow()
    {
        var state = RateState.Unknown
            .Merge(Count(4000), true, now)
            .Merge(Count(4010), true, now);
        await Assert.That(state.Remaining).IsEqualTo(4000d);
    }

    [Test]
    public async Task ALaterResetStartsANewWindow()
    {
        var state = RateState.Unknown
            .Merge(Count(10), true, now)
            .Merge(Count(4999, reset.AddHours(1)), true, now);
        await Assert.That(state.Remaining).IsEqualTo(4999d);
        await Assert.That(state.Reset).IsEqualTo(reset.AddHours(1));
    }

    [Test]
    public async Task AStragglerFromTheLastWindowIsIgnored()
    {
        var state = RateState.Unknown
            .Merge(Count(4999, reset.AddHours(1)), true, now)
            .Merge(Count(10), true, now);
        await Assert.That(state.Remaining).IsEqualTo(4999d);
    }

    [Test]
    public async Task RetryAfterOnASuccessPauses()
    {
        var state = RateState.Unknown.Merge(new(null, null, null, null, null, TimeSpan.FromSeconds(30)), true, now);
        await Assert.That(state.PausedUntil).IsEqualTo(now.AddSeconds(30));
    }

    [Test]
    public async Task RetryAfterOnAFailureIsLeftToTheException()
    {
        var state = RateState.Unknown.Merge(new(null, null, null, null, null, TimeSpan.FromSeconds(30)), false, now);
        await Assert.That(state.PausedUntil).IsNull();
    }

    [Test]
    public async Task ACountWithoutAResetIsCurrentForFiveMinutes()
    {
        var state = RateState.Unknown.Merge(new(200, 12.5, null, null, null, null), true, now);
        await Assert.That(state.Current(now.AddMinutes(4))).IsTrue();
        await Assert.That(state.Current(now.AddMinutes(6))).IsFalse();
    }

    [Test]
    public async Task ACountIsNotCurrentAfterItsReset()
    {
        var state = RateState.Unknown.Merge(Count(0), false, now);
        await Assert.That(state.Current(now)).IsTrue();
        await Assert.That(state.Current(reset.AddSeconds(1))).IsFalse();
    }
}
