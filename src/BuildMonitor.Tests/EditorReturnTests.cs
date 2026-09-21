/// <summary>
/// Where the connection editor goes when it closes. It is opened from the options page, and every
/// way out used to go to the builds page: adding a second connection took the whole way round
/// again, and the options opened again from the saved settings, losing whatever had been typed.
/// </summary>
public class EditorReturnTests
{
    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());

    static int ButtonIndex(SessionState state, CommandKind command) =>
        ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == command);

    /// <summary>
    /// The options page with a poll interval typed and not saved.
    /// </summary>
    static SessionState TypedOnOptions(RecordingActions actions) =>
        Apply(Fixtures.Options(), new(FieldChanges: [new(FormFields.PollInterval, "45")]), actions);

    static SessionState EditingJenkinsFromOptions(RecordingActions actions) =>
        Apply(TypedOnOptions(actions), new(ClickedField: FormFields.Connection(Fixtures.Jenkins.Id)), actions);

    static SessionState AddingWorkFromOptions(RecordingActions actions)
    {
        var state = Apply(TypedOnOptions(actions), new(ClickedField: FormFields.AddConnection), actions);
        return Apply(state, new(FieldChanges:
        [
            new(FormFields.Provider, "GitHub Actions"),
            new(FormFields.Auth, nameof(AuthMethod.Token)),
            new(FormFields.Name, "Work"),
            new(FormFields.Token, "ghp_secret")
        ]), actions);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CancelGoesBackToTheOptionsAsTheyWereLeft(bool escape)
    {
        var actions = new RecordingActions();
        var state = EditingJenkinsFromOptions(actions);
        await Assert.That(state.Page).IsEqualTo(Page.EditConnection);
        MonitorInput cancel = escape
            ? new(Key: CommandKind.CancelForm)
            : new(ClickedButton: ButtonIndex(state, CommandKind.CancelForm));
        state = Apply(state, cancel, actions);
        await Assert.That(state.Page).IsEqualTo(Page.Options);
        await Assert.That(state.Form!.Value(FormFields.PollInterval)).IsEqualTo("45");
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// The page's list is of the connections as they are now, so the one just added is on it.
    /// </summary>
    [Test]
    public async Task SavingANewConnectionGoesBackToTheOptions()
    {
        var actions = new RecordingActions();
        var state = AddingWorkFromOptions(actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.Save)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Options);
        await Assert.That(state.Status).IsEqualTo("Saved Work");
        await Assert.That(state.Form!.Value(FormFields.PollInterval)).IsEqualTo("45");
        var listed = ScreenBuilder.Build(state, Fixtures.Now).Form!.Fields
            .Where(_ => _.Kind == FieldKind.EditRow)
            .Select(_ => _.Label);
        await Assert.That(listed).Contains("Work");
    }

    [Test]
    public async Task RemovingGoesBackToTheOptions()
    {
        var actions = new RecordingActions();
        var state = EditingJenkinsFromOptions(actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.RemoveConnection)), actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.ConfirmRemoveConnection)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Options);
        await Assert.That(state.Status).IsEqualTo("Removed Jenkins");
        await Assert.That(state.Form!.Value(FormFields.PollInterval)).IsEqualTo("45");
        await Assert.That(state.Settings.Connections.Any(_ => _.Id == Fixtures.Jenkins.Id)).IsFalse();
    }

    /// <summary>
    /// The options are built at their save on the settings as they are then, so a connection added
    /// while they waited is kept, and so is what was typed on them before.
    /// </summary>
    [Test]
    public async Task SavingTheOptionsAfterwardsKeepsBoth()
    {
        var actions = new RecordingActions();
        var state = AddingWorkFromOptions(actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.Save)), actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.Save)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
        await Assert.That(state.Settings.PollIntervalSeconds).IsEqualTo(45);
        await Assert.That(state.Settings.Connections.Any(_ => _.Name == "Work")).IsTrue();
    }

    /// <summary>
    /// A sign in is a page drawn over the editor, which it hands back when it is done. The options
    /// the editor came from go with it, so the save after a sign in still goes back to them.
    /// </summary>
    [Test]
    public async Task ASignInOnTheWayKeepsTheWayBack()
    {
        var actions = new RecordingActions();
        var state = Apply(TypedOnOptions(actions), new(ClickedField: FormFields.AddConnection), actions);
        state = Apply(state, new(FieldChanges:
        [
            new(FormFields.Provider, "GitHub Actions"),
            new(FormFields.Auth, nameof(AuthMethod.Device)),
            new(FormFields.Name, "Work")
        ]), actions);
        var flow = Guid.NewGuid();
        state = MonitorSession.BeginSignIn(state, ConnectionDraft.Build(Fixtures.ConnectionForm(state)), AuthMethod.Device, flow);
        state = MonitorSession.SignInCompleted(state, flow, "simon");
        await Assert.That(state.Page).IsEqualTo(Page.AddConnection);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.Save)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Options);
        await Assert.That(state.Form!.Value(FormFields.PollInterval)).IsEqualTo("45");
    }

    /// <summary>
    /// An editor opened from anywhere but the options page has no options to go back to.
    /// </summary>
    [Test]
    public async Task AnEditorOpenedElsewhereGoesToTheBuilds()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.ConnectionEdit(), new(Key: CommandKind.CancelForm), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
    }
}
