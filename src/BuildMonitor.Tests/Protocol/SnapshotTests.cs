public class SnapshotTests
{
    [Test]
    public async Task PipelinesCountTheirRuns()
    {
        var pipelines = Snapshot.Pipelines(Fixtures.WatchOnly());
        await Assert.That(pipelines.Select(_ => $"{_.Key} {_.Runs}"))
            .IsEquivalentTo(["gh/DiffEngine/test.yml 1", "gh/DiffEngine/docs.yml 1", "gh/Verify/test.yml 2"]);
    }

    [Test]
    [Arguments("gh/Verify/test.yml/feature/inline")]
    [Arguments("gh/Verify/test.yml/main")]
    [Arguments("gh/Verify/test.yml")]
    public async Task RunsResolveABuildKeyOrAPipelineKey(string key)
    {
        var runs = Snapshot.Runs(Fixtures.WithBuilds(), key, Fixtures.Now);
        await Assert.That(runs!.Select(_ => $"{_.Key} #{_.Run}"))
            .IsEquivalentTo(["gh/Verify/test.yml/feature/inline #77", "gh/Verify/test.yml/main #76"]);
    }

    [Test]
    [Arguments("gh/Verify/test.yml/")]
    [Arguments("gh/Verify")]
    [Arguments("jenkins/Verify/test.yml")]
    public async Task RunsOfNoPipelineAreNull(string key) =>
        await Assert.That(Snapshot.Runs(Fixtures.WithBuilds(), key, Fixtures.Now)).IsNull();
}
