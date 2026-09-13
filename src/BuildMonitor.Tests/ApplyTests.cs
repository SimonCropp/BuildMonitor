/// <summary>
/// The applier is the one place a click becomes a consequence, so these pin what each kind of
/// input asks of the world.
/// </summary>
public class ApplyTests
{
    [Test]
    public async Task ClickingALinkOpensIt()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.WithBuilds(), new(ClickedLinkRow: 2, ClickedLink: LinkKind.PullRequest), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["OpenUrl https://github.com/VerifyTests/Verify/pull/42"]);
        await Assert.That(state.SelectedRow).IsEqualTo(2);
    }

    [Test]
    public async Task ClickingRetryAsksForARetry()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.WithBuilds(), new(ClickedActionRow: 2, ClickedAction: RowAction.Retry), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["Retry gh/Verify/test.yml/feature/inline"]);
        await Assert.That(state.Status).IsEqualTo("Retrying test.yml #77");
    }

    [Test]
    public async Task RetryOnARunningBuildIsIgnored()
    {
        var actions = new RecordingActions();
        Apply(Fixtures.WithBuilds(), new(ClickedActionRow: 1, ClickedAction: RowAction.Retry), actions);
        await Assert.That(actions.Calls).IsEmpty();
    }

    [Test]
    public async Task CancelFromTheMenu()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.WithBuilds(), new(RightClickedRow: 1), actions);
        var cancelIndex = state.Menu!.Items.ToList().FindIndex(_ => _.Command == CommandKind.Cancel);
        state = Apply(state, new(ClickedMenuItem: cancelIndex), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["Cancel gh/DiffEngine/test.yml/main"]);
        await Assert.That(state.Menu).IsNull();
    }

    [Test]
    public async Task CopyBuildUrlUsesTheWindowClipboard()
    {
        var window = new FakeWindow();
        var state = MonitorSession.SelectRow(Fixtures.WithBuilds(), 1);
        state = InputApplier.Apply(state, new(Key: CommandKind.CopyBuildUrl), new RecordingActions().Actions, window);
        await Assert.That(window.Calls).IsEquivalentTo(["SetClipboard https://example.com/gh/DiffEngine/test.yml/1234"]);
        await Assert.That(state.Status).IsEqualTo("Copied build URL");
    }

    [Test]
    public async Task CloseHidesRatherThanExits()
    {
        var window = new FakeWindow();
        var state = InputApplier.Apply(Fixtures.WithBuilds(), new(CloseRequested: true), new RecordingActions().Actions, window);
        await Assert.That(state.Hidden).IsTrue();
        await Assert.That(state.Exit).IsFalse();
        await Assert.That(window.Calls).IsEquivalentTo(["SetHidden True"]);
    }

    [Test]
    public async Task TrayIconClickShowsTheWindow()
    {
        var window = new FakeWindow();
        var state = InputApplier.Apply(Fixtures.Hidden(), new(TrayIconClicked: true), new RecordingActions().Actions, window);
        await Assert.That(state.Hidden).IsFalse();
        await Assert.That(window.Calls).IsEquivalentTo(["SetHidden False", "Focus"]);
    }

    [Test]
    public async Task TrayExitQuits()
    {
        var state = Apply(Fixtures.WithBuilds(), new(TrayItem: TrayMenu.Exit), new());
        await Assert.That(state.Exit).IsTrue();
    }

    [Test]
    public async Task TrayBuildItemsAct()
    {
        var actions = new RecordingActions();
        var failed = Fixtures.WithBuilds().Builds.First(_ => _.Status == BuildStatus.Failed);
        Apply(Fixtures.WithBuilds(), new(TrayItem: TrayMenu.BuildItem(failed, TrayMenu.RetryAction)), actions);
        Apply(Fixtures.WithBuilds(), new(TrayItem: TrayMenu.BuildItem(failed, TrayMenu.OpenAction)), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(
        [
            "Retry gh/Verify/test.yml/feature/inline",
            "OpenUrl https://example.com/gh/Verify/test.yml/77"
        ]);
    }

    [Test]
    public async Task TrayOptionsOpensTheWindowOnOptions()
    {
        var window = new FakeWindow();
        var state = InputApplier.Apply(Fixtures.Hidden(), new(TrayItem: TrayMenu.Options), new RecordingActions().Actions, window);
        await Assert.That(state.Page).IsEqualTo(Page.Options);
        await Assert.That(state.Hidden).IsFalse();
    }

    [Test]
    public async Task SavingOptionsPersistsAndRegistersStartup()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.Options(), new(FieldChanges: [new(FormFields.RunAtStartup, "true"), new(FormFields.PollInterval, "45")]), actions);
        state = Apply(state, new(ClickedButton: 0), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
        await Assert.That(state.Settings.PollIntervalSeconds).IsEqualTo(45);
        await Assert.That(actions.Calls).IsEquivalentTo(["SetRunAtLogin True", "SaveSettings"]);
        await Assert.That(actions.SavedSettings!.RunAtStartup).IsTrue();
    }

    [Test]
    public async Task InvalidOptionsStayOnThePage()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.Options(), new(FieldChanges: [new(FormFields.PollInterval, "1")]), actions);
        state = Apply(state, new(ClickedButton: 0), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Options);
        await Assert.That(state.Form!.Error).IsEqualTo("The poll interval must be between 5 and 3600 seconds.");
        await Assert.That(actions.Calls).IsEmpty();
    }

    [Test]
    public async Task SavingAConnectionStoresTheTokenAndRefreshes()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.ConnectionNew(), new(FieldChanges:
        [
            new(FormFields.Provider, "GitHub Actions"),
            new(FormFields.Auth, nameof(AuthMethod.Token)),
            new(FormFields.Name, "Work"),
            new(FormFields.Token, "ghp_secret")
        ]), actions);
        var save = ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == CommandKind.Save);
        state = Apply(state, new(ClickedButton: save), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
        await Assert.That(state.Settings.Connections.Length).IsEqualTo(4);
        await Assert.That(actions.Calls).IsEquivalentTo(["StoreSecret connection:draft1", "SaveSettings", "Refresh draft1"]);
        await Assert.That(actions.SavedSettings!.Connections.Last().Name).IsEqualTo("Work");
    }

    [Test]
    public async Task RemovingAConnectionDeletesItsSecrets()
    {
        var actions = new RecordingActions();
        var state = Fixtures.ConnectionEdit();
        var remove = ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == CommandKind.RemoveConnection);
        state = Apply(state, new(ClickedButton: remove), actions);
        await Assert.That(state.Settings.Connections.Any(_ => _.Id == Fixtures.Jenkins.Id)).IsFalse();
        await Assert.That(actions.Calls).IsEquivalentTo(["DeleteSecret connection:jenkins", "DeleteSecret connection:jenkins:refresh", "SaveSettings"]);
    }

    [Test]
    public async Task CancellingANewSignedInConnectionDeletesItsToken()
    {
        var actions = new RecordingActions();
        var state = Fixtures.SignInDevice();
        state = MonitorSession.SignInCompleted(state, state.SignIn!.FlowId, "simon");
        state = Apply(state, new(Key: CommandKind.CancelForm), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
        await Assert.That(actions.Calls).IsEquivalentTo(["DeleteSecret connection:draft1", "DeleteSecret connection:draft1:refresh"]);
    }

    [Test]
    public async Task SignInStartsAFlow()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.ConnectionNew(), new(FieldChanges: [new(FormFields.Provider, "GitHub Actions")]), actions);
        state = Apply(state, new(ClickedButton: 0), actions);
        await Assert.That(state.Page).IsEqualTo(Page.SignIn);
        await Assert.That(actions.Calls).IsEquivalentTo(["SignIn github Browser"]);
    }

    [Test]
    public async Task ListRowClickEditsTheConnection()
    {
        var state = Apply(Fixtures.Options(), new(ClickedField: FormFields.Connection(Fixtures.Octopus.Id)), new());
        await Assert.That(state.Page).IsEqualTo(Page.Connection);
        await Assert.That(state.Form!.EditingConnectionId).IsEqualTo(Fixtures.Octopus.Id);
    }

    [Test]
    public async Task RefreshOnAHeaderRefreshesThatConnection()
    {
        var actions = new RecordingActions();
        var state = MonitorSession.SelectRow(Fixtures.WithBuilds(), 0);
        Apply(state, new(Key: CommandKind.Refresh), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["Refresh gh"]);
    }

    [Test]
    public async Task InputClearsTheStatus()
    {
        var state = MonitorSession.SetStatus(Fixtures.WithBuilds(), "Done");
        state = Apply(state, new(Key: CommandKind.NextRow), new());
        await Assert.That(state.Status).IsEqualTo("");
    }

    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());
}
