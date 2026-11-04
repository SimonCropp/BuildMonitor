public class RunSetTests
{
    /// <summary>
    /// The push's running and queued runs share one row, where the first of them sorts, and count
    /// the passed and failed runs that have left it. The failure keeps a row of its own; the run of
    /// an earlier commit and the one naming no commit are not the push.
    /// </summary>
    [Test]
    public Task OnePushsRunsShareARunSet() =>
        Verify(RowProjection.Rows(Fixtures.WithPullRequestPush()).Select(_ => $"{_.Kind} {_.Build?.Key} {_.Group?.Id} members={_.Members.Length} passed={_.Passed} failed={_.Failed}"))
            .Snapshot(
                """
                [
                  Build jenkins/build-all/main  members=0 passed=0 failed=0,
                  Build gh/Verify/nightly.yml/simon/argument-polyfills  members=0 passed=0 failed=0,
                  Build octo/Projects-1/  members=0 passed=0 failed=0,
                  Group  run:gh/verifytests/verify/simon/argument-polyfills/f00d members=4 passed=1 failed=1,
                  Build gh/DiffEngine/test.yml/main  members=0 passed=0 failed=0,
                  Build gh/Verify/bench.yml/simon/argument-polyfills  members=0 passed=0 failed=0,
                  Build jenkins/nightly/  members=0 passed=0 failed=0,
                  Build gh/Verify/vsix.yml/simon/argument-polyfills  members=0 passed=0 failed=0,
                  Build gh/Verify/test.yml/feature/inline  members=0 passed=0 failed=0,
                  Build gh/Verify/lint.yml/simon/argument-polyfills  members=0 passed=0 failed=0,
                  Build gh/DiffEngine/docs.yml/main  members=0 passed=0 failed=0
                ]
                """);

    [Test]
    public Task AClosedRunSet() =>
        Verify(Fixtures.Render(Fixtures.WithPullRequestPush()));

    [Test]
    public Task AnOpenRunSet() =>
        Verify(Fixtures.Render(Opened(Fixtures.WithPullRequestPush())));

    [Test]
    public async Task ARunSetIsRunningWithTheBarOfItsSlowestRun()
    {
        var row = ScreenBuilder.Build(Fixtures.WithPullRequestPush(), Fixtures.Now).Builds!.Rows.First(_ => _.Kind == RowKind.Group);
        await Assert.That(row.Status).IsEqualTo(BuildStatus.Running);
        await Assert.That(row.DetailText).IsEqualTo("3 running, 1 queued, 1 passed, 1 failed simon/argument-polyfills");
        await Assert.That(row.Author).IsNotEmpty();
        await Assert.That(row.Tooltip(RowPart.Timing)).StartsWith("Last to finish: ");
    }

    [Test]
    public async Task ARunSetIsNotAPassingGroup()
    {
        var state = Fixtures.WithPullRequestPush();
        var set = RowProjection.Rows(state).First(_ => _.Kind == RowKind.Group);
        await Assert.That(set.Group!.IsRunSet).IsTrue();
        await Assert.That(set.Group.Id).IsNotEqualTo(new GroupKey("Verify").Id);
    }

    [Test]
    public async Task ARunSetsMenuOpensItsBranchAndPullRequest()
    {
        var state = Fixtures.WithPullRequestPush();
        var menu = MonitorSession.OpenMenu(state, Fixtures.RowOf(state, _ => _.Group is {IsRunSet: true})).Menu!;
        await Assert.That(menu.Items.Select(_ => _.Label)).Contains("Open pull request");
        await Assert.That(menu.Items.Select(_ => _.Label)).Contains("Open branch");
        await Assert.That(menu.Items.Any(_ => _.Label.StartsWith("Group by"))).IsFalse();
    }

    [Test]
    public async Task ARunSetsBranchOpensTheSharedBranch()
    {
        var actions = new RecordingActions();
        var state = Fixtures.WithPullRequestPush();
        var row = Fixtures.RowOf(state, _ => _.Group is {IsRunSet: true});
        InputApplier.Apply(state, new(ClickedChipRow: row, ClickedChip: ChipKind.Branch), actions.Actions, new FakeWindow());
        await Assert.That(actions.Calls).Contains("OpenUrl https://github.com/VerifyTests/Verify/tree/simon/argument-polyfills");
    }

    /// <summary>
    /// A finished push's id means nothing, so the next toggle drops it rather than keeping every
    /// push ever opened in the settings.
    /// </summary>
    [Test]
    public async Task ToggleDropsTheIdsOfFinishedRunSets()
    {
        var stale = $"{GroupKey.RunSetPrefix}gh/verifytests/verify/old/cafe";
        var state = Fixtures.WithPullRequestPush();
        state = state with
        {
            Settings = state.Settings with
            {
                OpenGroups = state.Settings.OpenGroups.Add(stale)
            }
        };
        var opened = Opened(state);
        await Assert.That(opened.Settings.OpenGroups.Contains(stale)).IsFalse();
        await Assert.That(opened.Settings.OpenGroups.Count(_ => _.StartsWith(GroupKey.RunSetPrefix))).IsEqualTo(1);
    }

    static SessionState Opened(SessionState state) =>
        MonitorSession.ToggleGroup(state, RowProjection.Rows(state).First(_ => _.Group is {IsRunSet: true}).Group!);
}
