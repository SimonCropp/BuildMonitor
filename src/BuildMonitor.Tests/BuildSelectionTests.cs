public class BuildSelectionTests
{
    [Test]
    public async Task TheDefaultBranchHeadsItsPipeline()
    {
        var selected = BuildSelection.Select(Fixtures.GitHubBuildsOnMain(), true);
        await Verify(Describe(selected))
            .Snapshot(
                """
                [
                  DiffEngine/test.yml main #1234 Running | lanes: none | folded: none,
                  Verify/test.yml main #76 Succeeded | lanes: feature/inline #77 Failed | folded: none,
                  DiffEngine/docs.yml main #300 Succeeded | lanes: none | folded: none
                ]
                """);
    }

    [Test]
    public async Task WithNoDefaultBranchTheNewestRunHeads()
    {
        var selected = BuildSelection.Select(Fixtures.GitHubBuilds(), true);
        await Verify(Describe(selected))
            .Snapshot(
                """
                [
                  DiffEngine/test.yml main #1234 Running | lanes: none | folded: none,
                  Verify/test.yml feature/inline #77 Failed | lanes: none | folded: main #76 Succeeded,
                  DiffEngine/docs.yml main #300 Succeeded | lanes: none | folded: none
                ]
                """);
    }

    /// <summary>
    /// A default branch with no run held, as when every run the window holds is a pull request's,
    /// leaves the pipeline headed by its newest run rather than by nothing.
    /// </summary>
    [Test]
    public async Task WithNoRunOnTheDefaultBranchTheNewestRunHeads()
    {
        var builds = Fixtures.GitHubBuilds()
            .Where(_ => _.PipelineId == "Verify/test.yml" && _.Branch != "main")
            .Select(_ => _ with { DefaultBranch = "main" });
        var selected = BuildSelection.Select(builds, true);
        await Assert.That(selected.Single().Head!.RunNumber).IsEqualTo("77");
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
        var selected = Shown(BuildSelection.Select(builds.SetItem(1, running), true));
        await Assert.That(selected.Count(_ => _.PipelineId == "Verify/test.yml")).IsEqualTo(1);

        var main = builds[2] with
        {
            Status = BuildStatus.Running,
            Finished = null,
            Started = Fixtures.Now - TimeSpan.FromHours(3)
        };
        var both = Shown(BuildSelection.Select(builds.SetItem(2, main), true));
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
        var selected = Shown(BuildSelection.Select(builds.Add(stuck), true));
        await Assert.That(selected.Count(_ => _.PipelineId == "Verify/test.yml")).IsEqualTo(1);
    }

    /// <summary>
    /// A branch's newest run that passed, or was cancelled, is history: it goes to the fold rather
    /// than to a row.
    /// </summary>
    [Test]
    public async Task SettledLanesFold()
    {
        var builds = Fixtures.GitHubBuildsOnMain();
        var cancelled = builds[1] with
        {
            Branch = "feature/cancelled",
            RunNumber = "78",
            Status = BuildStatus.Cancelled
        };
        var passed = builds[1] with
        {
            Branch = "feature/passed",
            RunNumber = "79",
            Status = BuildStatus.Succeeded
        };
        var selected = BuildSelection.Select(builds.AddRange(cancelled, passed), true);
        var verify = selected.Single(_ => _.Head!.PipelineId == "Verify/test.yml");
        await Assert.That(Describe(verify.Lanes)).IsEqualTo("feature/inline #77 Failed");
        await Assert.That(Describe(verify.Folded)).IsEqualTo("feature/cancelled #78 Cancelled, feature/passed #79 Succeeded");
    }

    /// <summary>
    /// AppVeyor builds a pull request's branch and then the pull request: two runs of one branch,
    /// only one of which names the pull request. The lane keeps its PR whichever is the newer.
    /// </summary>
    [Test]
    public async Task ALaneBorrowsThePullRequestAnOlderRunNamed()
    {
        var builds = Fixtures.GitHubBuildsOnMain();
        var branchBuild = builds[1] with
        {
            RunNumber = "78",
            Started = Fixtures.Now - TimeSpan.FromMinutes(10),
            Finished = Fixtures.Now - TimeSpan.FromMinutes(5),
            PullRequestNumber = null,
            PullRequestUrl = null
        };
        var selected = BuildSelection.Select(builds.Add(branchBuild), true);
        var lane = selected.Single(_ => _.Head!.PipelineId == "Verify/test.yml").Lanes.Single();
        await Assert.That(lane.RunNumber).IsEqualTo("78");
        await Assert.That(lane.PullRequestNumber).IsEqualTo("42");
        await Assert.That(lane.PullRequestUrl).IsEqualTo("https://github.com/VerifyTests/Verify/pull/42");
    }

    [Test]
    public async Task ALaneKeepsItsOwnPullRequest()
    {
        var builds = Fixtures.GitHubBuildsOnMain();
        var newer = builds[1] with
        {
            RunNumber = "78",
            Started = Fixtures.Now - TimeSpan.FromMinutes(10),
            Finished = Fixtures.Now - TimeSpan.FromMinutes(5),
            PullRequestNumber = "43",
            PullRequestUrl = "https://github.com/VerifyTests/Verify/pull/43"
        };
        var selected = BuildSelection.Select(builds.Add(newer), true);
        var lane = selected.Single(_ => _.Head!.PipelineId == "Verify/test.yml").Lanes.Single();
        await Assert.That(lane.PullRequestNumber).IsEqualTo("43");
    }

    [Test]
    public async Task DefaultBranchOnlyDropsTheExtras()
    {
        var builds = Fixtures.GitHubBuildsOnMain();
        var main = builds[2]
            with
            {
                Status = BuildStatus.Running,
                Finished = null,
                Started = Fixtures.Now - TimeSpan.FromHours(3)
            };
        var selected = BuildSelection.Select(builds.SetItem(2, main), false);
        var verify = selected.Single(_ => _.Head!.PipelineId == "Verify/test.yml");
        await Assert.That(verify.Head!.RunNumber).IsEqualTo("76");
        await Assert.That(verify.Lanes).IsEmpty();
        await Assert.That(verify.Folded).IsEmpty();
    }

    static List<Build> Shown(ImmutableArray<PipelineBuilds> pipelines) =>
        [..pipelines.SelectMany(_ => _.Shown)];

    static IEnumerable<string> Describe(ImmutableArray<PipelineBuilds> pipelines) =>
        pipelines.Select(_ => $"{_.Head!.PipelineId} {Describe(_.Head)} | lanes: {Describe(_.Lanes)} | folded: {Describe(_.Folded)}");

    static string Describe(ImmutableArray<Build> builds)
    {
        if (builds.IsEmpty)
        {
            return "none";
        }

        return string.Join(", ", builds.Select(Describe));
    }

    static string Describe(Build build) =>
        $"{build.Branch} #{build.RunNumber} {build.Status}";
}
