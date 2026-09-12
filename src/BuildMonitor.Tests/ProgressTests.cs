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
    public async Task NoEstimateShowsElapsedAndNoBar()
    {
        var build = Running(now - TimeSpan.FromMinutes(2) - TimeSpan.FromSeconds(5));
        var (fraction, text) = Progress.Compute(build, null, now);
        await Assert.That(fraction).IsEqualTo(-1);
        await Assert.That(text).IsEqualTo("02:05");
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
    public async Task ProviderDurationBeatsHistory()
    {
        var build = Running(now - TimeSpan.FromMinutes(1)) with { Estimate = new(TimeSpan.FromMinutes(4), null, null) };
        var estimate = Estimator.Estimate(build, ImmutableDictionary<string, TimeSpan>.Empty.Add(build.PipelineKey, TimeSpan.FromMinutes(10)));
        await Assert.That(estimate).IsEqualTo(TimeSpan.FromMinutes(4));
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
    [Arguments(0, "00:00")]
    [Arguments(65, "01:05")]
    [Arguments(3600, "1:00:00")]
    [Arguments(3725, "1:02:05")]
    public async Task Formats(int seconds, string expected) =>
        await Assert.That(Progress.Format(TimeSpan.FromSeconds(seconds))).IsEqualTo(expected);

    [Test]
    [Arguments(5, "5s")]
    [Arguments(90, "1m")]
    [Arguments(7200, "2h")]
    [Arguments(200000, "2d")]
    public async Task Ages(int seconds, string expected) =>
        await Assert.That(Progress.Age(TimeSpan.FromSeconds(seconds))).IsEqualTo(expected);

    static Build Running(DateTimeOffset started) =>
        Fixtures.Build("c", "p", "Pipeline", "repo", "main", "1", BuildStatus.Running, started: started);
}
