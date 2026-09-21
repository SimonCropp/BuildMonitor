/// <summary>
/// The version, documentation, logs and update, on a page of their own: each acts the moment it is
/// clicked, which among the options, under a Save and a Cancel, read as waiting for the Save.
/// </summary>
public class AboutPageTests
{
    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());

    static int ButtonIndex(SessionState state, CommandKind command) =>
        ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == command);

    /// <summary>
    /// The options with a poll interval typed and not saved, then their About.
    /// </summary>
    static SessionState OverTypedOptions(RecordingActions actions)
    {
        var state = Apply(Fixtures.Options(), new(FieldChanges: [new(FormFields.PollInterval, "45")]), actions);
        return Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.OpenAbout)), actions);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task BackGoesToTheOptionsAsTheyWereLeft(bool escape)
    {
        var actions = new RecordingActions();
        var state = OverTypedOptions(actions);
        await Assert.That(state.Page).IsEqualTo(Page.About);
        MonitorInput back = escape
            ? new(Key: CommandKind.CancelForm)
            : new(ClickedButton: ButtonIndex(state, CommandKind.CancelForm));
        state = Apply(state, back, actions);
        await Assert.That(state.Page).IsEqualTo(Page.Options);
        await Assert.That(state.Form!.Value(FormFields.PollInterval)).IsEqualTo("45");
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// An update called off goes back the way it came, and from there to the options, edits and
    /// all. Update used to leave the options for its own page, whose Cancel went to the builds.
    /// </summary>
    [Test]
    public async Task ACancelledUpdateGoesBackToAbout()
    {
        var actions = new RecordingActions();
        var state = OverTypedOptions(actions);
        state = Apply(state, new(ClickedField: FormFields.Update), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Update);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.CancelForm)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.About);
        state = Apply(state, new(Key: CommandKind.CancelForm), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Options);
        await Assert.That(state.Form!.Value(FormFields.PollInterval)).IsEqualTo("45");
        await Assert.That(actions.Calls).IsEquivalentTo(["RunningServers"]);
    }

    /// <summary>
    /// The logs and a new issue open outside the window, so the page stays where it is.
    /// </summary>
    [Test]
    public async Task LogsAndIssuesLeaveThePageAlone()
    {
        var actions = new RecordingActions();
        var state = OverTypedOptions(actions);
        state = Apply(state, new(ClickedField: FormFields.OpenLogs), actions);
        state = Apply(state, new(ClickedField: FormFields.RaiseIssue), actions);
        await Assert.That(state.Page).IsEqualTo(Page.About);
        await Assert.That(actions.Calls).IsEquivalentTo(["OpenLogs", "RaiseIssue"]);
    }

    /// <summary>
    /// Only the options open it, being the page its Back goes to.
    /// </summary>
    [Test]
    public async Task NothingButTheOptionsOpensIt()
    {
        var state = Fixtures.Filters();
        await Assert.That(MonitorSession.OpenAbout(state)).IsSameReferenceAs(state);
    }

    /// <summary>
    /// What is left on the options is settings: nothing there acts before its Save.
    /// </summary>
    [Test]
    public async Task TheOptionsHoldOnlySettings()
    {
        var kinds = ScreenBuilder.Build(Fixtures.Options(), Fixtures.Now).Form!.Fields.Select(_ => _.Kind).ToList();
        await Assert.That(kinds).DoesNotContain(FieldKind.Button);
        await Assert.That(kinds).DoesNotContain(FieldKind.EditRow);
        await Assert.That(kinds).DoesNotContain(FieldKind.Link);
    }
}
