public class RowProjectionTests
{
    [Test]
    public async Task AProjectionIsForAStateThatOnlyMovedTheSelection()
    {
        var state = Fixtures.WithGreenProject();
        var pipelines = RowProjection.Pipelines(state);
        var builds = new SortedBuilds(state.Settings, state.Connections, state.Builds, state.Verdicts, pipelines, RowProjection.Builds(state));
        var rows = new ProjectedRows(pipelines, state.Connections, state.Settings.OpenGroups, state.Search, state.Settings.GroupPrefixes, state.Settings.GroupByOrg, []);
        var moved = MonitorSession.SelectRow(state, 1);
        await Assert.That(builds.IsFor(moved)).IsTrue();
        await Assert.That(rows.IsFor(moved, pipelines)).IsTrue();
    }

    [Test]
    public async Task AProjectionIsNotForAStateWithOtherInputs()
    {
        var state = Fixtures.WithGreenProject();
        var sorted = RowProjection.Pipelines(state);
        var builds = new SortedBuilds(state.Settings, state.Connections, state.Builds, state.Verdicts, sorted, RowProjection.Builds(state));
        var rows = new ProjectedRows(sorted, state.Connections, state.Settings.OpenGroups, state.Search, state.Settings.GroupPrefixes, state.Settings.GroupByOrg, []);
        await Assert.That(
                rows.IsFor(MonitorSession.ToggleGroup(state, Fixtures.VerifyPassing), sorted))
            .IsFalse();
        await Assert.That(
                rows.IsFor(
                    state
                        with
                        {
                            Search = "nuget"
                        },
                    sorted))
            .IsFalse();
        await Assert.That(
                rows.IsFor(
                    state
                        with
                        {
                            Settings = state.Settings
                                with
                                {
                                    GroupPrefixes = ["Verify"]
                                }
                        },
                    sorted))
            .IsFalse();
        await Assert.That(
                rows.IsFor(state, [.. sorted]))
            .IsFalse();
        await Assert.That(
                builds.IsFor(
                    state
                        with
                        {
                            Builds = [.. state.Builds]
                        }))
            .IsFalse();
        await Assert.That(
                builds.IsFor(
                    state
                        with
                        {
                            Settings = state.Settings
                                with
                                {
                                    ShowOtherBranches = !state.Settings.ShowOtherBranches
                                }
                        }))
            .IsFalse();
        // An answer about a branch can fold its row, so the rows are sorted again.
        await Assert.That(
                builds.IsFor(
                    state
                        with
                        {
                            Verdicts = state.Verdicts.Add("key", new(BranchFate.Merged, Fixtures.Now))
                        }))
            .IsFalse();
    }

    /// <summary>
    /// Every row sorts by its own status: Verify's pull requests go up with the running, queued and
    /// failed rows, and its passing main to the bottom with the green ones.
    /// </summary>
    [Test]
    public Task EachRowSortsByItsOwnStatus() =>
        Verify(RowProjection.Rows(Fixtures.WithLanes()).Select(_ => $"{_.Kind} {_.Build?.Key} {_.Build?.Status}"))
            .Snapshot(
                """
                [
                  Build jenkins/build-all/main Running,
                  Build octo/Projects-1/ Running,
                  Build gh/Verify/test.yml/dependabot/nuget/src/Polyfill-9.1.0 Running,
                  Build gh/DiffEngine/test.yml/main Running,
                  Build jenkins/nightly/ Queued,
                  Build gh/Verify/test.yml/feature/docs Queued,
                  Build gh/Verify/test.yml/feature/inline Failed,
                  Build gh/Verify/test.yml/main Succeeded,
                  Build gh/DiffEngine/docs.yml/main Succeeded
                ]
                """);

    [Test]
    public async Task ProjectingAgainGivesTheSameRows()
    {
        var state = Fixtures.WithGreenProject();
        var rows = Shape(RowProjection.Rows(state));
        var toggled = RowProjection.Rows(MonitorSession.ToggleGroup(state, Fixtures.VerifyPassing));
        await Assert.That(Shape(toggled)).IsNotEquivalentTo(rows);
        await Assert.That(Shape(RowProjection.Rows(state))).IsEquivalentTo(rows);
    }

    [Test]
    public async Task ShowingTheWindowBringsBackTheRows() =>
        await Assert.That(Fixtures.Render(MonitorSession.Show(Fixtures.Hidden())))
            .IsEqualTo(Fixtures.Render(Fixtures.WithBuilds()));

    static List<string> Shape(ImmutableArray<Row> rows) =>
        rows.Select(_ => $"{_.Kind} {_.Build?.Key} {_.Group?.Id} {_.Expanded} {_.Members.Length}").ToList();
}
