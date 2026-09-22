/// <summary>
/// The Undo an exclude leaves in the footer. An exclude saves at once and takes its rows with it,
/// so without one, a slip on the menu meant the filters page: find the filter, remove it, save.
/// </summary>
public class ExcludeUndoTests
{
    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());

    static int ButtonIndex(SessionState state, CommandKind command) =>
        ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == command);

    /// <summary>
    /// The failed row's branch, excluded from its menu as a user would.
    /// </summary>
    static SessionState Excluded(RecordingActions actions, SessionState? start = null)
    {
        var builds = start ?? Fixtures.WithBuilds();
        var row = Fixtures.RowOf(builds, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");
        var state = Apply(builds, new(RightClickedRow: row), actions);
        var item = state.Menu!.Items.ToList().FindIndex(_ => _.Command == CommandKind.ExcludeBranch);
        return Apply(state, new(ClickedMenuItem: item), actions);
    }

    /// <summary>
    /// The message and its Undo, last in the footer, after the buttons it must not move.
    /// </summary>
    [Test]
    public Task TheFooterAfterAnExclude() =>
        Verify(Fixtures.Render(Excluded(new())));

    [Test]
    public async Task AnExcludeOffersUndo()
    {
        var actions = new RecordingActions();
        var state = Excluded(actions);
        await Assert.That(state.Status).IsEqualTo("Excluded feature/inline branch");
        var undo = ScreenBuilder.Buttons(state)[^1];
        await Assert.That(undo.Command).IsEqualTo(CommandKind.UndoExclude);
        await Assert.That(undo.Tooltip).IsEqualTo("Show the feature/inline branch again");
        await Assert.That(actions.Calls).IsEquivalentTo(["SaveSettings"]);
    }

    [Test]
    public async Task UndoTakesItBack()
    {
        var actions = new RecordingActions();
        var before = Fixtures.WithBuilds();
        var state = Excluded(actions, before);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.UndoExclude)), actions);
        await Assert.That(state.Settings.Filters).IsEquivalentTo(before.Settings.Filters);
        await Assert.That(state.Status).IsEqualTo("Showing the feature/inline branch again");
        await Assert.That(ButtonIndex(state, CommandKind.UndoExclude)).IsEqualTo(-1);
        await Assert.That(RowProjection.Rows(state).Any(_ => _.Build?.Key == "gh/Verify/test.yml/feature/inline")).IsTrue();
        await Assert.That(actions.Calls).IsEquivalentTo(["SaveSettings", "SaveSettings"]);
    }

    /// <summary>
    /// A filter the user had already stays, even one hiding the same rows: only what the exclude
    /// added comes out.
    /// </summary>
    [Test]
    public async Task UndoLeavesTheUsersOwnFilters()
    {
        var actions = new RecordingActions();
        var own = new Filter(FilterKind.Prefix, FilterTarget.Branch, "feature/");
        var builds = Fixtures.WithBuilds();
        var start = MonitorSession.ApplySettings(
            builds,
            builds.Settings with
            {
                Filters = [own]
            });
        // The user's filter hides the failed row, so the exclude is asked of another branch.
        var row = Fixtures.RowOf(start, _ => _.Build?.Key == "gh/DiffEngine/test.yml/main");
        var state = Apply(start, new(RightClickedRow: row), actions);
        state = Apply(state, new(ClickedMenuItem: state.Menu!.Items.ToList().FindIndex(_ => _.Command == CommandKind.ExcludeBranch)), actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.UndoExclude)), actions);
        await Assert.That(state.Settings.Filters).IsEquivalentTo([own]);
    }

    /// <summary>
    /// The Undo lives as long as the message beside it, which the next input clears.
    /// </summary>
    [Test]
    public async Task AnyOtherInputForgetsIt()
    {
        var actions = new RecordingActions();
        var state = Excluded(actions);
        state = Apply(state, new(ScrollDelta: 1), actions);
        await Assert.That(state.Undo).IsNull();
        await Assert.That(ButtonIndex(state, CommandKind.UndoExclude)).IsEqualTo(-1);
        await Assert.That(state.Settings.Filters.Any(_ => _.Target == FilterTarget.Branch)).IsTrue();
    }

    /// <summary>
    /// The click is resolved again once the Undo has gone, so a button before it has to keep its
    /// index: the Undo is last for that reason.
    /// </summary>
    [Test]
    public async Task AnotherButtonStillActsWhileItShows()
    {
        var actions = new RecordingActions();
        var state = Excluded(actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.Hide)), actions);
        await Assert.That(state.Hidden).IsTrue();
        await Assert.That(state.Undo).IsNull();
        await Assert.That(state.Settings.Filters.Any(_ => _.Target == FilterTarget.Branch)).IsTrue();
    }
}
