/// <summary>
/// Where the connection editor goes when it closes. It is opened from the connections page, and
/// every way out used to go to the builds page: adding a second connection took the whole way
/// round again.
/// </summary>
public class EditorReturnTests
{
    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());

    static int ButtonIndex(SessionState state, CommandKind command) =>
        ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == command);

    static IEnumerable<string> Listed(SessionState state) =>
        ScreenBuilder.Build(state, Fixtures.Now).Form!.Fields
            .Where(_ => _.Kind == FieldKind.EditRow)
            .Select(_ => _.Label);

    static SessionState EditingJenkins(RecordingActions actions) =>
        Apply(Fixtures.Connections(), new(ClickedField: FormFields.Connection(Fixtures.Jenkins.Id)), actions);

    /// <summary>
    /// A new connection typed into the editor the connections page opened. A token only for the
    /// method that takes one: a typed token is stored whatever the method.
    /// </summary>
    static SessionState Adding(RecordingActions actions, AuthMethod method)
    {
        var state = Fixtures.Connections();
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.AddConnection)), actions);
        List<FieldChange> changes =
        [
            new(FormFields.Provider, "GitHub Actions"),
            new(FormFields.Auth, method.ToString()),
            new(FormFields.Name, "Work")
        ];
        if (method == AuthMethod.Token)
        {
            changes.Add(new(FormFields.Token, "ghp_secret"));
        }

        return Apply(state, new(FieldChanges: changes), actions);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CancelGoesBackToTheConnections(bool escape)
    {
        var actions = new RecordingActions();
        var state = EditingJenkins(actions);
        await Assert.That(state.Page).IsEqualTo(Page.EditConnection);
        MonitorInput cancel = escape
            ? new(Key: CommandKind.CancelForm)
            : new(ClickedButton: ButtonIndex(state, CommandKind.CancelForm));
        state = Apply(state, cancel, actions);
        await Assert.That(state.Page).IsEqualTo(Page.Connections);
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// The page's list is of the connections as they are now, so the one just added is on it.
    /// </summary>
    [Test]
    public async Task SavingANewConnectionGoesBackToTheConnections()
    {
        var actions = new RecordingActions();
        var state = Adding(actions, AuthMethod.Token);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.Save)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Connections);
        await Assert.That(state.Status).IsEqualTo("Saved Work");
        await Assert.That(Listed(state)).Contains("Work");
    }

    [Test]
    public async Task RemovingGoesBackToTheConnections()
    {
        var actions = new RecordingActions();
        var state = EditingJenkins(actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.RemoveConnection)), actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.ConfirmRemoveConnection)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Connections);
        await Assert.That(state.Status).IsEqualTo("Removed Jenkins");
        await Assert.That(Listed(state)).DoesNotContain("Jenkins");
    }

    /// <summary>
    /// A sign in is a page drawn over the editor, which it hands back when it is done. The page the
    /// editor came from goes with it, so the save after a sign in still goes back there.
    /// </summary>
    [Test]
    public async Task ASignInOnTheWayKeepsTheWayBack()
    {
        var actions = new RecordingActions();
        var state = Adding(actions, AuthMethod.Device);
        var flow = Guid.NewGuid();
        state = MonitorSession.BeginSignIn(state, ConnectionDraft.Build(Fixtures.ConnectionForm(state)), AuthMethod.Device, flow);
        state = MonitorSession.SignInCompleted(state, flow, "simon");
        await Assert.That(state.Page).IsEqualTo(Page.AddConnection);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.Save)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Connections);
    }

    /// <summary>
    /// An editor opened from anywhere but the connections page, such as a row's menu or the
    /// footer's Sign in, goes back to the builds it was opened over.
    /// </summary>
    [Test]
    public async Task AnEditorOpenedElsewhereGoesToTheBuilds()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.ConnectionEdit(), new(Key: CommandKind.CancelForm), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
    }
}
