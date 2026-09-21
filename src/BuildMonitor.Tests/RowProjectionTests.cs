public class RowProjectionTests
{
    [Test]
    public async Task AProjectionIsForAStateThatOnlyMovedTheSelection()
    {
        var state = Fixtures.WithGreenProject();
        var sorted = RowProjection.Builds(state);
        var builds = new SortedBuilds(state.Settings, state.Connections, state.Builds, sorted);
        var rows = new ProjectedRows(sorted, state.Connections, state.Settings.OpenGroups, state.Search, state.Settings.GroupPrefixes, []);
        var moved = MonitorSession.SelectRow(state, 1);
        await Assert.That(builds.IsFor(moved)).IsTrue();
        await Assert.That(rows.IsFor(moved, sorted)).IsTrue();
    }

    [Test]
    public async Task AProjectionIsNotForAStateWithOtherInputs()
    {
        var state = Fixtures.WithGreenProject();
        var sorted = RowProjection.Builds(state);
        var builds = new SortedBuilds(state.Settings, state.Connections, state.Builds, sorted);
        var rows = new ProjectedRows(sorted, state.Connections, state.Settings.OpenGroups, state.Search, state.Settings.GroupPrefixes, []);
        await Assert.That(rows.IsFor(MonitorSession.ToggleGroup(state, Fixtures.VerifyPassing), sorted)).IsFalse();
        await Assert.That(rows.IsFor(state with { Search = "nuget" }, sorted)).IsFalse();
        await Assert.That(rows.IsFor(state with { Settings = state.Settings with { GroupPrefixes = ["Verify"] } }, sorted)).IsFalse();
        await Assert.That(rows.IsFor(state, [..sorted])).IsFalse();
        await Assert.That(builds.IsFor(state with { Builds = [..state.Builds] })).IsFalse();
        await Assert.That(builds.IsFor(state with { Settings = state.Settings with { ShowOtherBranches = !state.Settings.ShowOtherBranches } })).IsFalse();
    }

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
