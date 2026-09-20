public class HistoryCutoffTests
{
    static readonly DateTimeOffset now = new(2026, 9, 15, 16, 30, 0, TimeSpan.FromHours(10));

    [Test]
    public async Task StartOfTheUtcDay() =>
        await Assert.That(HistoryCutoff.Of(now, 30)).IsEqualTo(new(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));

    [Test]
    [Arguments("Succeeded", -40, false)]
    [Arguments("Failed", -40, false)]
    [Arguments("Succeeded", -10, true)]
    [Arguments("Running", -40, true)]
    [Arguments("Queued", -40, true)]
    public async Task KeepsRecentAndActive(string name, int days, bool kept)
    {
        var status = Enum.Parse<BuildStatus>(name);
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
