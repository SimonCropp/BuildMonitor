/// <summary>
/// Where a row's menu draws its lines: between what to look at, what changes a service's builds,
/// what changes this machine or this window, and what hides rows for good. Run together, Retry and
/// Cancel build sat between Copy log and Refresh, and the excludes followed Refresh straight on.
/// </summary>
public class MenuSeparatorTests
{
    static List<string> LinedAbove(SessionState state, Func<Row, bool> row) =>
        MonitorSession.OpenMenu(state, Fixtures.RowOf(state, row)).Menu!.Items
            .Where(_ => _.SeparatorAbove)
            .Select(_ => _.Label)
            .ToList();

    [Test]
    public async Task AFailedBuildsRetryStandsApart() =>
        await Assert.That(LinedAbove(Fixtures.WithBuilds(), _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline"))
            .IsEquivalentTo(["Retry", "Refresh", "Exclude action: test.yml"]);

    [Test]
    public async Task ARunningBuildsCancelStandsApart() =>
        await Assert.That(LinedAbove(Fixtures.WithBuilds(), _ => _.Build?.Key == "gh/DiffEngine/test.yml/main"))
            .IsEquivalentTo(["Cancel build", "Refresh", "Exclude action: test.yml"]);

    /// <summary>
    /// A kind a build does not offer leaves no line behind: a passing build has nothing that
    /// changes a service's builds, so there is one line fewer rather than two lines together.
    /// </summary>
    [Test]
    public async Task AKindNotOfferedLeavesNoLine() =>
        await Assert.That(LinedAbove(Fixtures.WithBuilds(), _ => _.Build?.Key == "gh/DiffEngine/docs.yml/main"))
            .IsEquivalentTo(["Refresh", "Exclude action: docs.yml"]);

    /// <summary>
    /// Everything a group's own menu offers changes this window, so it has no lines at all.
    /// </summary>
    [Test]
    public async Task AGroupsMenuHasNone() =>
        await Assert.That(LinedAbove(Fixtures.WithTwoFailures(), _ => _.Kind == RowKind.Group)).IsEmpty();

    /// <summary>
    /// The overflow drop down stands in for chips, not the menu, and offers no kinds to part.
    /// </summary>
    [Test]
    public async Task TheOverflowDropDownHasNone()
    {
        var state = Fixtures.WithBuilds();
        var row = Fixtures.RowOf(state, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");
        var menu = MonitorSession.OpenOverflow(state, row, ChipKind.Retry).Menu!;
        await Assert.That(menu.Items.Any(_ => _.SeparatorAbove)).IsFalse();
    }
}
