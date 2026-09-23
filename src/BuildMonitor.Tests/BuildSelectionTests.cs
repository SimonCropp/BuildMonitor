public class BuildSelectionTests
{
    [Test]
    public async Task TheDefaultBranchHeadsItsPipeline()
    {
        var selected = Select(Fixtures.GitHubBuildsOnMain());
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
        var selected = Select(Fixtures.GitHubBuilds());
        await Verify(Describe(selected))
            .Snapshot(
                """
                [
                  DiffEngine/test.yml main #1234 Running | lanes: none | folded: none,
                  Verify/test.yml feature/inline #77 Failed | lanes: none | folded: main #76 Succeeded Settled,
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
        var selected = Select(builds);
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
        var selected = Shown(Select(builds.SetItem(1, running)));
        await Assert.That(selected.Count(_ => _.PipelineId == "Verify/test.yml")).IsEqualTo(1);

        var main = builds[2] with
        {
            Status = BuildStatus.Running,
            Finished = null,
            Started = Fixtures.Now - TimeSpan.FromHours(3)
        };
        var both = Shown(Select(builds.SetItem(2, main)));
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
        var selected = Shown(Select(builds.Add(stuck)));
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
        var selected = Select(builds.AddRange(cancelled, passed));
        var verify = selected.Single(_ => _.Head!.PipelineId == "Verify/test.yml");
        await Assert.That(Describe(verify.Lanes)).IsEqualTo("feature/inline #77 Failed");
        await Assert.That(Describe(verify.Folded)).IsEqualTo("feature/cancelled #78 Cancelled Settled, feature/passed #79 Succeeded Settled");
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
        var selected = Select(builds.Add(branchBuild));
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
        var selected = Select(builds.Add(newer));
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
        var selected = Select(builds.SetItem(2, main), false);
        var verify = selected.Single(_ => _.Head!.PipelineId == "Verify/test.yml");
        await Assert.That(verify.Head!.RunNumber).IsEqualTo("76");
        await Assert.That(verify.Lanes).IsEmpty();
        await Assert.That(verify.Folded).IsEmpty();
    }

    /// <summary>
    /// A failed pull request that has been merged or closed since, or whose branch has been deleted,
    /// can no longer be fixed, so it folds into its pipeline's hover saying which.
    /// </summary>
    [Test]
    [Arguments(BranchFate.Merged, FoldReason.Merged)]
    [Arguments(BranchFate.Closed, FoldReason.Closed)]
    [Arguments(BranchFate.Deleted, FoldReason.Deleted)]
    public async Task AFailedBranchThatIsGoneFolds(BranchFate fate, FoldReason reason)
    {
        var builds = Fixtures.GitHubBuildsOnMain();
        var verify = VerifyPipeline(Select(builds, Answered(builds[1], fate, Fixtures.Now)));
        await Assert.That(verify.Lanes).IsEmpty();
        await Assert.That(Describe(verify.Folded)).IsEqualTo($"feature/inline #77 Failed {reason}");
    }

    /// <summary>
    /// An open pull request keeps its row however long ago it failed, even after main has built
    /// since: it is still waiting on a fix.
    /// </summary>
    [Test]
    public async Task AnOpenPullRequestKeepsItsRow()
    {
        var builds = WithMainBuiltSince();
        var verify = VerifyPipeline(Select(builds, Answered(builds[1], BranchFate.Open, Fixtures.Now), _ => false));
        await Assert.That(Describe(verify.Lanes)).IsEqualTo("feature/inline #77 Failed");
    }

    /// <summary>
    /// An answer given before the run started says nothing about it: the branch was pushed again, so
    /// its row stays until it is asked about again.
    /// </summary>
    [Test]
    public async Task AnAnswerFromBeforeTheRunSpeaksForNothing()
    {
        var builds = Fixtures.GitHubBuildsOnMain();
        var before = builds[1].Started!.Value - TimeSpan.FromMinutes(1);
        var verify = VerifyPipeline(Select(builds, Answered(builds[1], BranchFate.Deleted, before)));
        await Assert.That(Describe(verify.Lanes)).IsEqualTo("feature/inline #77 Failed");
    }

    /// <summary>
    /// A failed branch whose repository no connection can ask about folds once the pipeline's
    /// default branch has built since it failed, as it does where the one asked could not say.
    /// </summary>
    [Test]
    public async Task WithNoAnswerAFailedBranchFoldsOnceMainBuildsAfterIt()
    {
        var builds = WithMainBuiltSince();
        var unasked = VerifyPipeline(Select(builds, [], _ => false));
        await Assert.That(unasked.Lanes).IsEmpty();
        await Assert.That(Describe(unasked.Folded)).IsEqualTo("feature/inline #77 Failed Superseded");

        var unknown = VerifyPipeline(Select(builds, Answered(builds[1], BranchFate.Unknown, Fixtures.Now)));
        await Assert.That(Describe(unknown.Folded)).IsEqualTo("feature/inline #77 Failed Superseded");
    }

    /// <summary>
    /// Main building before the failure says nothing about the branch, and neither does a head that
    /// is only the newest run, where the default branch is not known.
    /// </summary>
    [Test]
    public async Task WithNoAnswerAFailedBranchKeepsItsRowUntilMainBuildsAfterIt()
    {
        var older = VerifyPipeline(Select(Fixtures.GitHubBuildsOnMain(), [], _ => false));
        await Assert.That(Describe(older.Lanes)).IsEqualTo("feature/inline #77 Failed");

        var unknownDefault = WithMainBuiltSince().Select(_ => _ with { DefaultBranch = null });
        var newest = VerifyPipeline(Select(unknownDefault, [], _ => false));
        await Assert.That(Describe(newest.Head!)).IsEqualTo("main #78 Succeeded");
        await Assert.That(Describe(newest.Lanes)).IsEqualTo("feature/inline #77 Failed");
    }

    /// <summary>
    /// A branch that can be asked about keeps its row until the answer comes, rather than folding on
    /// a guess the answer might take back.
    /// </summary>
    [Test]
    public async Task AFailedBranchWaitingOnItsAnswerKeepsItsRow()
    {
        var verify = VerifyPipeline(Select(WithMainBuiltSince(), [], _ => true));
        await Assert.That(Describe(verify.Lanes)).IsEqualTo("feature/inline #77 Failed");
    }

    /// <summary>
    /// <see cref="Fixtures.GitHubBuildsOnMain"/> with Verify's main built again after
    /// feature/inline failed.
    /// </summary>
    static ImmutableArray<Build> WithMainBuiltSince()
    {
        var builds = Fixtures.GitHubBuildsOnMain();
        return builds.Add(
            builds[2] with
            {
                RunNumber = "78",
                Started = Fixtures.Now - TimeSpan.FromMinutes(10),
                Finished = Fixtures.Now - TimeSpan.FromMinutes(5)
            });
    }

    static ImmutableDictionary<string, BranchVerdict> Answered(Build build, BranchFate fate, DateTimeOffset at) =>
        ImmutableDictionary<string, BranchVerdict>.Empty.Add(BranchVerdicts.KeyOf(build)!, new(fate, at));

    static PipelineBuilds VerifyPipeline(ImmutableArray<PipelineBuilds> pipelines) =>
        pipelines.Single(_ => _.Head!.PipelineId == "Verify/test.yml");

    /// <summary>
    /// Every repository could be asked about, and none has been answered yet, so no failed branch
    /// folds.
    /// </summary>
    static ImmutableArray<PipelineBuilds> Select(IEnumerable<Build> builds, bool showOtherBranches = true) =>
        BuildSelection.Select(builds, showOtherBranches, [], _ => true);

    static ImmutableArray<PipelineBuilds> Select(IEnumerable<Build> builds, ImmutableDictionary<string, BranchVerdict> verdicts, Func<Build, bool>? askable = null) =>
        BuildSelection.Select(builds, true, verdicts, askable ?? (_ => true));

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

    static string Describe(ImmutableArray<FoldedBranch> folded)
    {
        if (folded.IsEmpty)
        {
            return "none";
        }

        return string.Join(", ", folded.Select(_ => $"{Describe(_.Build)} {_.Reason}"));
    }

    static string Describe(Build build) =>
        $"{build.Branch} #{build.RunNumber} {build.Status}";
}
