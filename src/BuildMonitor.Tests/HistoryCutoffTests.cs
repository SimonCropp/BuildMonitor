public class HistoryCutoffTests
{
    static readonly DateTimeOffset now = new(2026, 9, 15, 16, 30, 0, TimeSpan.FromHours(10));

    [Test]
    public async Task StartOfTheUtcDay() =>
        await Assert.That(HistoryCutoff.Of(now, 30)).IsEqualTo(new(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));

    [Test]
    [Arguments(BuildStatus.Succeeded, -40, false)]
    [Arguments(BuildStatus.Failed, -40, false)]
    [Arguments(BuildStatus.Succeeded, -10, true)]
    [Arguments(BuildStatus.Running, -40, true)]
    [Arguments(BuildStatus.Queued, -40, true)]
    public async Task KeepsRecentAndActive(BuildStatus status, int days, bool kept)
    {
        var at = now.AddDays(days);
        var build = Build(status, at, status is BuildStatus.Running or BuildStatus.Queued ? null : at);
        await Assert.That(HistoryCutoff.Keeps(build, HistoryCutoff.Of(now, 30))).IsEqualTo(kept);
    }

    [Test]
    public async Task KeepsABuildStartedBeforeTheCutoffThatFinishedAfter() =>
        await Assert.That(HistoryCutoff.Keeps(Build(BuildStatus.Succeeded, now.AddDays(-40), now.AddDays(-10)), HistoryCutoff.Of(now, 30))).IsTrue();

    [Test]
    public async Task KeepsABuildWithNoTime() =>
        await Assert.That(HistoryCutoff.Keeps(Build(BuildStatus.Succeeded, null, null), HistoryCutoff.Of(now, 30))).IsTrue();

    static Build Build(BuildStatus status, DateTimeOffset? started, DateTimeOffset? finished) =>
        new("connection", "pipeline", "build.yml", "repo", "main", "1", status, null, started, started, finished, null, "https://example.com/1", null, null, null, null, null, null, false, false, "", "https://example.com");
}
