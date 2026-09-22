public class BuildSelectionTests
{
    [Test]
    public async Task LatestPerPipelinePlusActiveBranches()
    {
        var selected = BuildSelection.Select(Fixtures.GitHubBuilds(), true);
        await Verify(selected.Select(_ => $"{_.PipelineId} {_.Branch} #{_.RunNumber} {_.Status}"))
            .Snapshot(
                """
                [
                  DiffEngine/test.yml main #1234 Running,
                  Verify/test.yml feature/inline #77 Failed,
                  DiffEngine/docs.yml main #300 Succeeded
                ]
                """);
    }

    [Test]
    public async Task OtherBranchesOnlyWhenActive()
    {
        var builds = Fixtures.GitHubBuilds();
        var running = builds[1] with
        {
            Status = BuildStatus.Running,
            Finished = null,
            Started = Fixtures.Now
        };
        var selected = BuildSelection.Select(builds.SetItem(1, running), true);
        await Assert.That(selected.Count(_ => _.PipelineId == "Verify/test.yml")).IsEqualTo(1);

        var main = builds[2] with
        {
            Status = BuildStatus.Running,
            Finished = null,
            Started = Fixtures.Now - TimeSpan.FromHours(3)
        };
        var both = BuildSelection.Select(builds.SetItem(2, main), true);
        await Assert.That(both.Count(_ => _.PipelineId == "Verify/test.yml")).IsEqualTo(2);
    }

    [Test]
    public async Task OtherBranchIgnoresActiveRunSupersededOnThatBranch()
    {
        var builds = Fixtures.GitHubBuilds();
        var stuck = builds[2] with
        {
            RunNumber = "40",
            Status = BuildStatus.Queued,
            Finished = null,
            Started = Fixtures.Now - TimeSpan.FromDays(3)
        };
        var selected = BuildSelection.Select(builds.Add(stuck), true);
        await Assert.That(selected.Count(_ => _.PipelineId == "Verify/test.yml")).IsEqualTo(1);
    }

    [Test]
    public async Task DefaultBranchOnlyDropsTheExtras()
    {
        var builds = Fixtures.GitHubBuilds();
        var main = builds[2]
            with
            {
                Status = BuildStatus.Running,
                Finished = null,
                Started = Fixtures.Now - TimeSpan.FromHours(3)
            };
        var selected = BuildSelection.Select(builds.SetItem(2, main), false);
        await Assert.That(selected.Count(_ => _.PipelineId == "Verify/test.yml")).IsEqualTo(1);
    }
}
