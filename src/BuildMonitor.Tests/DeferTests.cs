/// <summary>
/// A broken build put off from its row's menu for a few days: its row, its red and its
/// notifications wait until then, or until the pipeline passes.
/// </summary>
public class DeferTests
{
    const string failedKey = "gh/Verify/test.yml/feature/inline";

    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());

    static int ButtonIndex(SessionState state, CommandKind command) =>
        ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == command);

    static bool Shown(SessionState state) =>
        RowProjection.Rows(state).Any(_ => _.Build?.Key == failedKey);

    static Build Failed(SessionState state) =>
        state.Builds.Single(_ => _.Key == failedKey);

    /// <summary>
    /// The failed row, deferred from its menu as a user would.
    /// </summary>
    static SessionState Deferred(RecordingActions actions, string label = "Defer 3 days")
    {
        var builds = Fixtures.WithBuilds();
        var state = Apply(builds, new(RightClickedRow: Fixtures.RowOf(builds, _ => _.Build?.Key == failedKey)), actions);
        var item = state.Menu!.Items.ToList().FindIndex(_ => _.Label == label);
        return Apply(state, new(ClickedMenuItem: item), actions);
    }

    [Test]
    public async Task AFailedBuildOffersEachSpan()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.OpenMenu(builds, Fixtures.RowOf(builds, _ => _.Build?.Key == failedKey));
        await Assert.That(state.Menu!.Items.Where(_ => _.Command == CommandKind.Defer).Select(_ => _.Label))
            .IsEquivalentTo(["Defer 1 day", "Defer 3 days", "Defer 7 days"]);
    }

    /// <summary>
    /// Only a failure has anything to put off.
    /// </summary>
    [Test]
    public async Task APassingBuildOffersNone()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.OpenMenu(builds, Fixtures.RowOf(builds, _ => _.Build?.Key == "gh/DiffEngine/docs.yml/main"));
        await Assert.That(state.Menu!.Items.Any(_ => _.Command == CommandKind.Defer)).IsFalse();
    }

    [Test]
    public async Task DeferringHidesTheRowAndSaves()
    {
        var actions = new RecordingActions();
        var state = Deferred(actions);
        await Assert.That(Shown(state)).IsFalse();
        await Assert.That(state.Status).IsEqualTo("Deferred the test.yml failure on feature/inline for 3 days");
        await Assert.That(actions.Calls).IsEquivalentTo(["SaveSettings"]);
        var deferral = actions.SavedSettings!.Deferrals.Single();
        await Assert.That(deferral.Key).IsEqualTo(failedKey);
        await Assert.That(deferral.Until - DateTimeOffset.UtcNow).IsGreaterThan(TimeSpan.FromDays(3) - TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task UndoTakesItBack()
    {
        var actions = new RecordingActions();
        var state = Deferred(actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.UndoExclude)), actions);
        await Assert.That(state.Settings.Deferrals).IsEmpty();
        await Assert.That(Shown(state)).IsTrue();
        await Assert.That(state.Status).IsEqualTo("Showing the test.yml failure on feature/inline again");
    }

    /// <summary>
    /// The row's red on the tray goes with it: a deferral that left the icon red would put off
    /// nothing.
    /// </summary>
    [Test]
    public async Task TheTrayNoLongerCountsIt()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.Defer(builds, Failed(builds), 1, Fixtures.Now);
        await Assert.That(RowProjection.Builds(state).Any(_ => _.Status == BuildStatus.Failed)).IsFalse();
    }

    /// <summary>
    /// A lane deferred goes on its own: the pipeline's row and its other lanes stay.
    /// </summary>
    [Test]
    public async Task DeferringALaneHidesOnlyIt()
    {
        var builds = Fixtures.WithLanes();
        var lane = RowProjection.Rows(builds).Single(_ => _ is {Kind: RowKind.Lane, Build.Status: BuildStatus.Failed}).Build!;
        var state = MonitorSession.Defer(builds, lane, 1, Fixtures.Now);
        var rows = RowProjection.Rows(state);
        await Assert.That(rows.Any(_ => _.Build?.Key == lane.Key)).IsFalse();
        await Assert.That(rows.Any(_ => _.Build?.Key == "gh/Verify/test.yml/main")).IsTrue();
        await Assert.That(rows.Count(_ => _.Kind == RowKind.Lane)).IsEqualTo(2);
    }

    /// <summary>
    /// A pipeline's own failure deferred leaves its lanes, and the first of them names the
    /// pipeline in its place: a lane with no named row over it reads as a run of the row above.
    /// </summary>
    [Test]
    public async Task ALaneOutlivesItsDeferredPipelineAndNamesIt()
    {
        var lanes = Fixtures.WithLanes();
        var runs = lanes.Builds.Where(_ => _.ConnectionId == Fixtures.GitHub.Id).ToList();
        var index = runs.FindIndex(_ => _.Key == "gh/Verify/test.yml/main");
        runs[index] = runs[index] with
        {
            Status = BuildStatus.Failed
        };
        var broken = MonitorSession.ApplyPoll(lanes, Fixtures.GitHub.Id, [], [.. runs], Fixtures.Now);
        var state = MonitorSession.Defer(broken, runs[index], 1, Fixtures.Now);
        var verify = RowProjection.Rows(state).Where(_ => _.Build?.RepoName == "VerifyTests/Verify").ToList();
        await Assert.That(verify.Select(_ => _.Kind)).IsEquivalentTo([RowKind.Build, RowKind.Lane, RowKind.Lane]);
        await Assert.That(verify[0].Build!.Branch).IsEqualTo("dependabot/nuget/src/Polyfill-9.1.0");
    }

    /// <summary>
    /// A deferred pipeline running again is worth watching, so only its failure is hidden.
    /// </summary>
    [Test]
    public async Task ARunningBuildIsShown()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.Defer(builds, Failed(builds), 1, Fixtures.Now);
        var running = Failed(builds) with
        {
            RunNumber = "78",
            Status = BuildStatus.Running,
            Started = Fixtures.Now,
            Finished = null
        };
        state = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [..Fixtures.GitHubBuilds(), running], Fixtures.Now);
        await Assert.That(Shown(state)).IsTrue();
        await Assert.That(state.Settings.Deferrals).IsNotEmpty();
    }

    /// <summary>
    /// A retry that fails the same way is the failure that was put off, not news.
    /// </summary>
    [Test]
    public async Task AnotherFailureIsNotAnnounced()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.ClearNotification(MonitorSession.Defer(builds, Failed(builds), 1, Fixtures.Now));
        var again = Failed(builds) with
        {
            RunNumber = "78",
            Started = Fixtures.Now,
            Finished = Fixtures.Now
        };
        state = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [..Fixtures.GitHubBuilds(), again], Fixtures.Now);
        await Assert.That(state.Notification).IsNull();
        await Assert.That(Shown(state)).IsFalse();
    }

    [Test]
    public async Task ItEndsWhenDue()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.Defer(builds, Failed(builds), 1, Fixtures.Now);
        state = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], Fixtures.GitHubBuilds(), Fixtures.Now + TimeSpan.FromDays(1));
        await Assert.That(state.Settings.Deferrals).IsEmpty();
        await Assert.That(Shown(state)).IsTrue();
    }

    /// <summary>
    /// A fix ends it early, so the pipeline's next break is shown rather than taken for the one
    /// that was put off.
    /// </summary>
    [Test]
    public async Task ItEndsWhenThePipelinePasses()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.Defer(builds, Failed(builds), 7, Fixtures.Now);
        var passed = Failed(builds) with
        {
            RunNumber = "78",
            Status = BuildStatus.Succeeded,
            Started = Fixtures.Now,
            Finished = Fixtures.Now
        };
        state = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [..Fixtures.GitHubBuilds(), passed], Fixtures.Now);
        await Assert.That(state.Settings.Deferrals).IsEmpty();
    }

    /// <summary>
    /// A poll that ends nothing leaves the settings the same instance, which every projection is
    /// cached against.
    /// </summary>
    [Test]
    public async Task APollThatEndsNothingKeepsTheSettings()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.Defer(builds, Failed(builds), 7, Fixtures.Now);
        var polled = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], Fixtures.GitHubBuilds(), Fixtures.Now);
        await Assert.That(ReferenceEquals(polled.Settings, state.Settings)).IsTrue();
    }

    [Test]
    public async Task DeferringAgainReplacesTheFirst()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.Defer(builds, Failed(builds), 7, Fixtures.Now);
        state = MonitorSession.Defer(state, Failed(builds), 1, Fixtures.Now);
        await Assert.That(state.Settings.Deferrals.Single().Until).IsEqualTo(Fixtures.Now.AddDays(1));
    }

    /// <summary>
    /// The filters page lists what is deferred, with how long is left, and is where one is ended
    /// early.
    /// </summary>
    [Test]
    public Task TheFiltersPageListsIt()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.Defer(builds, Failed(builds), 3, Fixtures.Now - TimeSpan.FromHours(1));
        return Verify(Fixtures.Render(MonitorSession.OpenFilters(state)));
    }

    [Test]
    public async Task TheFiltersPageEndsIt()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.OpenFilters(MonitorSession.Defer(builds, Failed(builds), 3, Fixtures.Now));
        state = Apply(state, new(ClickedField: FormFields.Deferral(0)), actions);
        state = Apply(state, new(Key: CommandKind.Save), actions);
        await Assert.That(state.Settings.Deferrals).IsEmpty();
        await Assert.That(Shown(state)).IsTrue();
        await Assert.That(actions.SavedSettings!.Deferrals).IsEmpty();
    }

    [Test]
    [Arguments(0.5, "1 hour left")]
    [Arguments(5, "5 hours left")]
    [Arguments(24, "1 day left")]
    [Arguments(25, "2 days left")]
    [Arguments(72, "3 days left")]
    public async Task Remaining(double hours, string expected)
    {
        var deferral = new Deferral(failedKey, "x", Fixtures.Now + TimeSpan.FromHours(hours));
        await Assert.That(Deferrals.Remaining(deferral, Fixtures.Now)).IsEqualTo(expected);
    }
}
