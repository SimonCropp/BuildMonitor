public class EstimatorTests
{
    [Test]
    public async Task ProviderDurationBeatsHistory()
    {
        var build = Running(new(TimeSpan.FromMinutes(4), null, null));
        var estimate = Estimator.Estimate(build, ImmutableDictionary<string, TimeSpan>.Empty.Add(build.PipelineKey, TimeSpan.FromMinutes(10)));
        await Assert.That(estimate).IsEqualTo(TimeSpan.FromMinutes(4));
    }

    [Test]
    public async Task HistoryWhenTheProviderGivesNoDuration()
    {
        var build = Running(new(null, 50, TimeSpan.FromMinutes(1)));
        var estimate = Estimator.Estimate(build, ImmutableDictionary<string, TimeSpan>.Empty.Add(build.PipelineKey, TimeSpan.FromMinutes(10)));
        await Assert.That(estimate).IsEqualTo(TimeSpan.FromMinutes(10));
    }

    [Test]
    public async Task NoEstimateWithoutDurationOrHistory() =>
        await Assert.That(Estimator.Estimate(Running(null), ImmutableDictionary<string, TimeSpan>.Empty)).IsNull();

    [Test]
    public async Task WindowFromProviderDurationIsThreeQuartersToAll()
    {
        var build = Running(new(TimeSpan.FromMinutes(4), null, null));
        var window = Estimator.Window(build, ImmutableDictionary<string, DurationRange>.Empty.Add(build.PipelineKey, new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(20))));
        await Assert.That(window).IsEqualTo(new DurationRange(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4)));
    }

    [Test]
    public async Task WindowFromHistoryWhenTheProviderGivesNoDuration()
    {
        var build = Running(null);
        var range = new DurationRange(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(7));
        var window = Estimator.Window(build, ImmutableDictionary<string, DurationRange>.Empty.Add(build.PipelineKey, range));
        await Assert.That(window).IsEqualTo(range);
    }

    [Test]
    public async Task NoWindowWithoutDurationOrHistory() =>
        await Assert.That(Estimator.Window(Running(null), ImmutableDictionary<string, DurationRange>.Empty)).IsNull();

    static Build Running(ProviderEstimate? estimate) =>
        Fixtures.Build("c", "p", "Pipeline", "repo", "main", "1", BuildStatus.Running, started: Fixtures.Now, estimate: estimate);
}
