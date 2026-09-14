public class PollScheduleTests
{
    static readonly DateTimeOffset now = Fixtures.Now;

    static ScheduleInput Input(
        IEnumerable<PollGroup> groups,
        IEnumerable<Build> builds,
        ImmutableDictionary<string, GroupMemory>? memory = null,
        RequestQuota? quota = null,
        TimeSpan? idleCap = null,
        RateState? rate = null,
        DateTimeOffset? pausedUntil = null,
        RequestBucket? bucket = null,
        ImmutableDictionary<string, TimeSpan>? medians = null) =>
        new(
            "gh",
            quota,
            idleCap,
            [..groups],
            memory ?? ImmutableDictionary<string, GroupMemory>.Empty,
            [..builds],
            medians ?? ImmutableDictionary<string, TimeSpan>.Empty,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(10),
            rate ?? RateState.Unknown,
            pausedUntil,
            bucket,
            false,
            now);

    static PollGroup Group(string key, params string[] pipelineIds) =>
        new(key, [..pipelineIds.Select(_ => new Pipeline(_, _, key, key, $"https://github.com/{key}"))]);

    static GroupMemory Fetched(TimeSpan ago, params string[] pipelineIds) =>
        GroupMemory.New with { LastAttempt = now - ago, FetchedPipelines = [..pipelineIds] };

    static Build Finished(string pipelineId, BuildStatus status, TimeSpan ago) =>
        Fixtures.Build("gh", pipelineId, pipelineId, "repo", "main", "1", status, started: now - ago, finished: now - ago);

    static Build Running(string pipelineId, TimeSpan ago, ProviderEstimate? estimate = null) =>
        Fixtures.Build("gh", pipelineId, pipelineId, "repo", "main", "2", BuildStatus.Running, started: now - ago, estimate: estimate);

    static Build Queued(string pipelineId, TimeSpan ago) =>
        Fixtures.Build("gh", pipelineId, pipelineId, "repo", "main", "3", BuildStatus.Queued, queued: now - ago);

    static (ScheduleReason Reason, TimeSpan Interval) Pipeline(Build build, TimeSpan? idleCap = null, ImmutableDictionary<string, TimeSpan>? medians = null) =>
        PollSchedule.PipelineInterval(Input([], [build], idleCap: idleCap, medians: medians), [build]);

    [Test]
    [Arguments(false, 10, 30)]
    [Arguments(false, 60, 120)]
    [Arguments(false, 720, 300)]
    [Arguments(true, 30, 30)]
    [Arguments(true, 120, 60)]
    [Arguments(true, 480, 240)]
    [Arguments(true, 720, 300)]
    public async Task AQuietPipelineSlowsWithTheTimeSinceItsLastBuild(bool failed, int minutesAgo, int seconds) =>
        await Assert.That(Pipeline(Finished("ci", failed ? BuildStatus.Failed : BuildStatus.Succeeded, TimeSpan.FromMinutes(minutesAgo))).Interval).IsEqualTo(TimeSpan.FromSeconds(seconds));

    [Test]
    public async Task ASuccessFromLastHourIsARecentSuccess() =>
        await Assert.That(Pipeline(Finished("ci", BuildStatus.Succeeded, TimeSpan.FromHours(1))).Reason).IsEqualTo(ScheduleReason.RecentSuccess);

    [Test]
    public async Task AQuietRepositoryOnBitbucketWaitsThirtyMinutes() =>
        await Assert.That(Pipeline(Finished("ci", BuildStatus.Succeeded, TimeSpan.FromDays(2)), TimeSpan.FromMinutes(30)))
            .IsEqualTo((ScheduleReason.Quiet, TimeSpan.FromMinutes(30)));

    [Test]
    [Arguments(2, 30)]
    [Arguments(5, 10)]
    [Arguments(7, 10)]
    public async Task ARunningBuildPollsFastNearItsUsualDuration(int minutesIn, int seconds)
    {
        var medians = ImmutableDictionary<string, TimeSpan>.Empty.Add("gh/ci", TimeSpan.FromMinutes(6));
        await Assert.That(Pipeline(Running("ci", TimeSpan.FromMinutes(minutesIn)), medians: medians).Interval).IsEqualTo(TimeSpan.FromSeconds(seconds));
    }

    [Test]
    public async Task ARunningBuildWithoutAnEstimateIsFinishing() =>
        await Assert.That(Pipeline(Running("ci", TimeSpan.FromMinutes(1)))).IsEqualTo((ScheduleReason.Finishing, TimeSpan.FromSeconds(10)));

    [Test]
    [Arguments(60, "Finishing")]
    [Arguments(300, "Running")]
    public async Task TheProvidersRemainingTimeBeatsTheMedian(int remainingSeconds, string reason) =>
        await Assert.That(Pipeline(Running("ci", TimeSpan.FromMinutes(1), new(null, 50, TimeSpan.FromSeconds(remainingSeconds)))).Reason).IsEqualTo(Enum.Parse<ScheduleReason>(reason));

    [Test]
    public async Task AQueuedBuildPollsAtTheInterval() =>
        await Assert.That(Pipeline(Queued("ci", TimeSpan.FromMinutes(1)))).IsEqualTo((ScheduleReason.Queued, TimeSpan.FromSeconds(30)));

    [Test]
    public async Task ABuildActiveForSevenHoursIsQuiet() =>
        await Assert.That(Pipeline(Running("ci", TimeSpan.FromHours(7)))).IsEqualTo((ScheduleReason.Quiet, TimeSpan.FromMinutes(5)));

    [Test]
    public async Task AGroupPollsAsOftenAsItsBusiestPipeline()
    {
        var group = Group("VerifyTests/Verify", "ci", "docs");
        var builds = new[] { Finished("ci", BuildStatus.Failed, TimeSpan.FromMinutes(30)), Finished("docs", BuildStatus.Succeeded, TimeSpan.FromHours(12)) };
        var memory = ImmutableDictionary<string, GroupMemory>.Empty.Add(group.Key, Fetched(TimeSpan.Zero, "ci", "docs"));
        var plan = PollSchedule.Group(Input([group], builds, memory), group);
        await Assert.That(plan.Interval).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(plan.Reason).IsEqualTo(ScheduleReason.RecentFailure);
    }

    [Test]
    public async Task ANewPipelineMakesItsGroupDue()
    {
        var group = Group("VerifyTests/Verify", "ci", "docs");
        var memory = ImmutableDictionary<string, GroupMemory>.Empty.Add(group.Key, Fetched(TimeSpan.FromSeconds(5), "ci"));
        var plan = PollSchedule.Group(Input([group], [], memory), group);
        await Assert.That(plan.DueAt).IsEqualTo(now);
        await Assert.That(plan.Reason).IsEqualTo(ScheduleReason.Unfetched);
    }

    [Test]
    public async Task ANudgeIsDueAtOnceThenKeepsTheIntervalShortForThreeMinutes()
    {
        var group = Group("VerifyTests/Verify", "ci");
        var builds = new[] { Finished("ci", BuildStatus.Succeeded, TimeSpan.FromDays(1)) };

        var nudged = Fetched(TimeSpan.FromMinutes(1), "ci") with { NudgedAt = now - TimeSpan.FromSeconds(5) };
        var dueNow = PollSchedule.Group(Input([group], builds, ImmutableDictionary<string, GroupMemory>.Empty.Add(group.Key, nudged)), group);
        await Assert.That(dueNow.DueAt).IsEqualTo(now);

        var afterFetch = Fetched(TimeSpan.Zero, "ci") with { NudgedAt = now - TimeSpan.FromMinutes(1) };
        var shortened = PollSchedule.Group(Input([group], builds, ImmutableDictionary<string, GroupMemory>.Empty.Add(group.Key, afterFetch)), group);
        await Assert.That((shortened.Reason, shortened.Interval)).IsEqualTo((ScheduleReason.Nudged, TimeSpan.FromSeconds(30)));

        var expired = Fetched(TimeSpan.Zero, "ci") with { NudgedAt = now - TimeSpan.FromMinutes(4) };
        var quietAgain = PollSchedule.Group(Input([group], builds, ImmutableDictionary<string, GroupMemory>.Empty.Add(group.Key, expired)), group);
        await Assert.That(quietAgain.Interval).IsEqualTo(TimeSpan.FromMinutes(5));
    }

    [Test]
    [Arguments(1, 30)]
    [Arguments(3, 120)]
    [Arguments(9, 600)]
    public async Task FailuresBackOffPerGroup(int failures, int seconds)
    {
        var group = Group("VerifyTests/Verify", "ci");
        var memory = Fetched(TimeSpan.Zero, "ci") with { Failures = failures };
        var plan = PollSchedule.Group(Input([group], [Queued("ci", TimeSpan.FromMinutes(1))], ImmutableDictionary<string, GroupMemory>.Empty.Add(group.Key, memory)), group);
        await Assert.That(plan.Interval).IsEqualTo(TimeSpan.FromSeconds(seconds));
    }

    [Test]
    [Arguments(4000, 1d)]
    [Arguments(500, 2.5)]
    [Arguments(50, 8d)]
    public async Task ADrainingQuotaStretchesIntervals(int remaining, double pressure) =>
        await Assert.That(PollSchedule.Pressure(new(5000, remaining, now.AddMinutes(40), false, null, now), now)).IsEqualTo(pressure);

    [Test]
    public async Task AQuotaResettingWithinAMinuteAddsNoPressure() =>
        await Assert.That(PollSchedule.Pressure(new(5000, 50, now.AddSeconds(30), false, null, now), now)).IsEqualTo(1d);

    [Test]
    public async Task NothingIsFetchedBeforeAPauseEnds()
    {
        var group = Group("VerifyTests/Verify", "ci");
        var plan = PollSchedule.Plan(Input([group], [], pausedUntil: now.AddMinutes(2)));
        await Assert.That(plan.Fetch).IsEmpty();
        await Assert.That(plan.WakeAt).IsEqualTo(now.AddMinutes(2));
    }

    [Test]
    public async Task SpreadIsStableAndWithinATenth()
    {
        var spreads = Enumerable.Range(0, 1000).Select(_ => PollSchedule.Spread("gh", $"repo{_}")).ToList();
        await Assert.That(PollSchedule.Spread("gh", "repo7")).IsEqualTo(spreads[7]);
        await Assert.That(spreads.All(_ => _ is >= -0.1 and <= 0.1)).IsTrue();
        await Assert.That(spreads.Max() - spreads.Min()).IsGreaterThan(0.19);
    }

    [Test]
    public async Task GroupsFetchedTogetherFallDueAcrossAMinute()
    {
        var groups = Enumerable.Range(0, 300).Select(_ => Group($"repo{_}", $"pipeline{_}")).ToList();
        var builds = groups.Select(_ => Finished(_.Pipelines[0].Id, BuildStatus.Succeeded, TimeSpan.FromDays(1)));
        var memory = groups.ToImmutableDictionary(_ => _.Key, _ => Fetched(TimeSpan.Zero, _.Pipelines[0].Id));
        var plan = PollSchedule.Plan(Input(groups, builds, memory));
        var due = plan.Groups.Select(_ => _.DueAt).ToList();
        await Assert.That(due.Max() - due.Min()).IsGreaterThan(TimeSpan.FromSeconds(50));
    }

    [Test]
    public async Task TheQuotaAdmitsTheMostUrgentGroupsFirst()
    {
        string[] keys = ["unfetched", "nudged", "finishing", "quiet", "unfetchedToo"];
        var groups = keys.Select(_ => Group(_, $"{_}-ci")).ToList();
        var builds = new[]
        {
            Finished("nudged-ci", BuildStatus.Succeeded, TimeSpan.FromDays(1)),
            Running("finishing-ci", TimeSpan.FromMinutes(1)),
            Finished("quiet-ci", BuildStatus.Succeeded, TimeSpan.FromDays(1))
        };
        var memory = ImmutableDictionary<string, GroupMemory>.Empty
            .Add("nudged", Fetched(TimeSpan.FromSeconds(30), "nudged-ci") with { NudgedAt = now - TimeSpan.FromSeconds(1) })
            .Add("finishing", Fetched(TimeSpan.FromSeconds(20), "finishing-ci"))
            .Add("quiet", Fetched(TimeSpan.FromMinutes(10), "quiet-ci"));
        var plan = PollSchedule.Plan(Input(groups, builds, memory, quota: new(3, TimeSpan.FromMinutes(1), 3)));
        await Assert.That(string.Join(',', plan.Fetch.Select(_ => _.Key))).IsEqualTo("finishing,nudged,unfetched");
        await Assert.That(string.Join(',', plan.Deferred)).IsEqualTo("unfetchedToo,quiet");
    }

    [Test]
    public async Task ADeferredGroupWakesWhenATokenRefills()
    {
        var group = Group("VerifyTests/Verify", "ci");
        // One request every ten seconds, and none saved.
        var quota = new RequestQuota(6, TimeSpan.FromMinutes(1), 1);
        var plan = PollSchedule.Plan(Input([group], [], quota: quota, bucket: new RequestBucket(0, now)));
        await Assert.That(plan.Deferred).IsEquivalentTo(new[] { group.Key });
        await Assert.That(plan.WakeAt).IsEqualTo(now.AddSeconds(10));
    }

    [Test]
    public Task DescribesAPlan()
    {
        var groups = new[] { Group("VerifyTests/Verify", "ci", "docs"), Group("VerifyTests/DiffEngine", "ci2"), Group("VerifyTests/New", "ci3") };
        var builds = new[]
        {
            Running("ci", TimeSpan.FromMinutes(2)),
            Finished("docs", BuildStatus.Succeeded, TimeSpan.FromHours(12)),
            Finished("ci2", BuildStatus.Failed, TimeSpan.FromHours(2))
        };
        var memory = ImmutableDictionary<string, GroupMemory>.Empty
            .Add("VerifyTests/Verify", Fetched(TimeSpan.FromSeconds(15), "ci", "docs"))
            .Add("VerifyTests/DiffEngine", Fetched(TimeSpan.FromSeconds(20), "ci2"));
        var plan = PollSchedule.Plan(Input(groups, builds, memory));
        return Verify(PollSchedule.Describe(plan, now))
            .Snapshot(
                """
                VerifyTests/Verify               Finishing         10s      now fetch
                VerifyTests/DiffEngine           RecentFailure      1m     +42s wait
                VerifyTests/New                  Unfetched          0s      now fetch
                wake +42s
                """);
    }
}
