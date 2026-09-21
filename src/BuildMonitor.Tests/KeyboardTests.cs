/// <summary>
/// What the keys added for commands that only a mouse could reach do, once a head has read them.
/// </summary>
public class KeyboardTests
{
    static SessionState Apply(SessionState state, MonitorInput input) =>
        InputApplier.Apply(state, input, new RecordingActions().Actions, new FakeWindow());

    /// <summary>
    /// The menu a right click opens, on the selected row, so everything it offers, Exclude and
    /// Group by prefix among it, has a way in from the keyboard.
    /// </summary>
    [Test]
    public async Task TheMenuKeyOpensTheSelectedRowsMenu()
    {
        var builds = Fixtures.WithBuilds();
        var row = Fixtures.RowOf(builds, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");
        var state = MonitorSession.SelectRow(builds, row);
        var keyed = Apply(state, new(Key: CommandKind.OpenMenu)).Menu!;
        var clicked = Apply(state, new(RightClickedRow: row)).Menu!;
        await Assert.That(keyed.Row).IsEqualTo(row);
        await Assert.That(keyed.Items.Select(_ => _.Label)).IsEquivalentTo(clicked.Items.Select(_ => _.Label));
    }

    [Test]
    public async Task TheMenuKeyDoesNothingOnAForm() =>
        await Assert.That(Apply(Fixtures.Options(), new(Key: CommandKind.OpenMenu)).Menu).IsNull();

    /// <summary>
    /// Enter on a group's row opens or closes it, as a click on it does: a group has no one build
    /// to open.
    /// </summary>
    [Test]
    public async Task EnterOnAGroupOpensIt()
    {
        var builds = Fixtures.WithTwoFailures();
        var row = Fixtures.RowOf(builds, _ => _.Kind == RowKind.Group);
        var state = Apply(MonitorSession.SelectRow(builds, row), new(Key: CommandKind.OpenBuild));
        await Assert.That(RowProjection.Rows(state)[row].Expanded).IsTrue();
    }
}
