public class ProgressTests
{
    static readonly DateTimeOffset now = Fixtures.Now;

    [Test]
    public async Task HistoryEstimateGivesCountdown()
    {
        var build = Running(now - TimeSpan.FromMinutes(3));
        var (fraction, text) = Progress.Compute(build, TimeSpan.FromMinutes(6), now);
        await Assert.That(fraction).IsEqualTo(0.5);
        await Assert.That(text).IsEqualTo("03:00 left");
    }

    [Test]
    public async Task OverRunShowsPlusAndCaps()
    {
        var build = Running(now - TimeSpan.FromMinutes(7));
        var (fraction, text) = Progress.Compute(build, TimeSpan.FromMinutes(6), now);
        await Assert.That(fraction).IsEqualTo(0.95);
        await Assert.That(text).IsEqualTo("+01:00");
    }

    [Test]
    public async Task ExactlyOnTheEstimateShowsNothingLeft()
    {
        var build = Running(now - TimeSpan.FromMinutes(6));
        var (fraction, text) = Progress.Compute(build, TimeSpan.FromMinutes(6), now);
        await Assert.That(fraction).IsEqualTo(0.95);
        await Assert.That(text).IsEqualTo("00:00 left");
    }

    [Test]
    public async Task NoEstimateShowsElapsedAndNoBar()
    {
        var build = Running(now - TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(5));
        var (fraction, text) = Progress.Compute(build, null, now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("02:05");
    }

    [Test]
    [Arguments(0)]
    [Arguments(-60)]
    public async Task AnEstimateOfNothingShowsElapsedAndNoBar(int seconds)
    {
        var build = Running(now - TimeSpan.FromMinutes(2));
        var (fraction, text) = Progress.Compute(build, TimeSpan.FromSeconds(seconds), now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("02:00");
    }

    [Test]
    public async Task ProviderRemainingWins()
    {
        var build = Running(now - TimeSpan.FromMinutes(1)) with { Estimate = new(null, 40, TimeSpan.FromSeconds(90)) };
        var (fraction, text) = Progress.Compute(build, TimeSpan.FromHours(1), now);
        await Assert.That(fraction).IsEqualTo(0.4);
        await Assert.That(text).IsEqualTo("01:30 left");
    }

    [Test]
    public async Task ProviderRemainingWithoutPercentIsTheShareElapsed()
    {
        var build = Running(now - TimeSpan.FromMinutes(3)) with { Estimate = new(null, null, TimeSpan.FromMinutes(1)) };
        var (fraction, text) = Progress.Compute(build, null, now);
        await Assert.That(fraction).IsEqualTo(0.75);
        await Assert.That(text).IsEqualTo("01:00 left");
    }

    [Test]
    public async Task ProviderRemainingOfNothingOnABuildJustStartedFillsTheBar()
    {
        // Worked out, nothing elapsed and nothing left is 0/0, a NaN fraction.
        var build = Running(now) with { Estimate = new(null, null, TimeSpan.Zero) };
        var (fraction, text) = Progress.Compute(build, null, now);
        await Assert.That(fraction).IsEqualTo(0.95);
        await Assert.That(text).IsEqualTo("00:00 left");
    }

    [Test]
    public async Task ProviderOverrunShowsPlusAndCaps()
    {
        // Worked out, a minute over on a thirty second run is a negative fraction, an empty bar.
        var build = Running(now - TimeSpan.FromSeconds(30)) with { Estimate = new(null, null, TimeSpan.FromMinutes(-1)) };
        var (fraction, text) = Progress.Compute(build, null, now);
        await Assert.That(fraction).IsEqualTo(0.95);
        await Assert.That(text).IsEqualTo("+01:00");
    }

    [Test]
    [Arguments(40, 0.4)]
    [Arguments(150, 0.95)]
    [Arguments(-10, 0)]
    public async Task ProviderPercentWithoutRemainingShowsElapsed(double percent, double expected)
    {
        var build = Running(now - TimeSpan.FromMinutes(2)) with { Estimate = new(null, percent, null) };
        var (fraction, text) = Progress.Compute(build, TimeSpan.FromHours(1), now);
        await Assert.That(fraction).IsEqualTo(expected);
        await Assert.That(text).IsEqualTo("02:00");
    }

    [Test]
    public async Task AStartInTheFutureCountsAsJustStarted()
    {
        var build = Running(now + TimeSpan.FromMinutes(1));
        var (fraction, text) = Progress.Compute(build, TimeSpan.FromMinutes(6), now);
        await Assert.That(fraction).IsEqualTo(0);
        await Assert.That(text).IsEqualTo("06:00 left");
    }

    [Test]
    public async Task ARunningBuildWithNoStartCountsFromItsQueueTime()
    {
        var build = Build(BuildStatus.Running, queued: now - TimeSpan.FromMinutes(2));
        var (fraction, text) = Progress.Compute(build, null, now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("02:00");
    }

    [Test]
    public async Task ARunningBuildWithNoTimesCountsFromNow()
    {
        var (fraction, text) = Progress.Compute(Build(BuildStatus.Running), TimeSpan.FromMinutes(6), now);
        await Assert.That(fraction).IsEqualTo(0);
        await Assert.That(text).IsEqualTo("06:00 left");
    }

    [Test]
    public async Task QueuedShowsHowLongItHasWaited()
    {
        var (fraction, text) = Progress.Compute(Build(BuildStatus.Queued, queued: now - TimeSpan.FromMinutes(5)), TimeSpan.FromMinutes(6), now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("queued 5m");
    }

    [Test]
    public async Task AQueuedBuildWithNoTimesShowsNoAge()
    {
        var (fraction, text) = Progress.Compute(Build(BuildStatus.Queued), null, now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("queued");
    }

    [Test]
    public async Task FinishedShowsAge()
    {
        var build = Running(now - TimeSpan.FromHours(3)) with { Status = BuildStatus.Succeeded, Finished = now - TimeSpan.FromHours(2) };
        var (fraction, text) = Progress.Compute(build, null, now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("2h ago");
    }

    [Test]
    public async Task AFinishedBuildWithNoFinishTimeCountsFromItsStart()
    {
        var (fraction, text) = Progress.Compute(Build(BuildStatus.Failed, started: now - TimeSpan.FromHours(3)), null, now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("3h ago");
    }

    [Test]
    public async Task AFinishedBuildWithOnlyAQueueTimeCountsFromIt()
    {
        var (fraction, text) = Progress.Compute(Build(BuildStatus.Cancelled, queued: now - TimeSpan.FromDays(2)), null, now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("2d ago");
    }

    [Test]
    public async Task AFinishedBuildWithNoTimesShowsNothing()
    {
        var (fraction, text) = Progress.Compute(Build(BuildStatus.Succeeded), null, now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("");
    }

    [Test]
    [Arguments(0, "00:00")]
    [Arguments(59.9, "00:59")]
    [Arguments(65, "01:05")]
    [Arguments(3599.9, "59:59")]
    [Arguments(3600, "1:00:00")]
    [Arguments(3725, "1:02:05")]
    [Arguments(90000, "25:00:00")]
    public async Task Formats(double seconds, string expected) =>
        await Assert.That(Progress.Format(TimeSpan.FromSeconds(seconds))).IsEqualTo(expected);

    [Test]
    [Arguments(-5, "0s")]
    [Arguments(5, "5s")]
    [Arguments(59, "59s")]
    [Arguments(60, "1m")]
    [Arguments(90, "1m")]
    [Arguments(3599, "59m")]
    [Arguments(3600, "1h")]
    [Arguments(7200, "2h")]
    [Arguments(86399, "23h")]
    [Arguments(86400, "1d")]
    [Arguments(200000, "2d")]
    public async Task Ages(int seconds, string expected) =>
        await Assert.That(Progress.Age(TimeSpan.FromSeconds(seconds))).IsEqualTo(expected);

    static Build Running(DateTimeOffset started) =>
        Fixtures.Build("c", "p", "Pipeline", "repo", "main", "1", BuildStatus.Running, started: started);

    static Build Build(BuildStatus status, DateTimeOffset? queued = null, DateTimeOffset? started = null) =>
        Fixtures.Build("c", "p", "Pipeline", "repo", "main", "1", status, queued: queued, started: started);
}
