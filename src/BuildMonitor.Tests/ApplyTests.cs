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
        var builds = Fixtures.WithBuilds();
        var row = FailedRow(builds);
        var state = Apply(builds, new(ClickedChipRow: row, ClickedChip: ChipKind.PullRequest), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["OpenUrl https://github.com/VerifyTests/Verify/pull/42"]);
        await Assert.That(state.SelectedRow).IsEqualTo(row);
    }

    [Test]
    [Arguments(nameof(ChipKind.Build), "https://example.com/gh/Verify/test.yml/77")]
    [Arguments(nameof(ChipKind.Branch), "https://github.com/VerifyTests/Verify/tree/feature/inline")]
    public async Task ClickingANameInTheRowOpensIt(string link, string url)
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        var row = FailedRow(builds);
        var state = Apply(builds, new(ClickedChipRow: row, ClickedChip: Enum.Parse<ChipKind>(link)), actions);
        await Assert.That(actions.Calls).IsEquivalentTo([$"OpenUrl {url}"]);
        await Assert.That(state.SelectedRow).IsEqualTo(row);
    }

    [Test]
    public async Task TypingInTheFilterBoxNarrowsTheRows()
    {
        var state = Apply(Fixtures.WithBuilds(), new(Search: "inline"), new());
        await Assert.That(state.Search).IsEqualTo("inline");
        await Assert.That(MonitorSession.SelectedBuild(state)?.Key).IsEqualTo("gh/Verify/test.yml/feature/inline");
    }

    [Test]
    public async Task AClickInTheFrameTheFilterChangedActsOnTheRowThatWasOnScreen()
    {
        // Filtered first, row 0 would be the failure, which cannot be cancelled.
        var actions = new RecordingActions();
        var state = Apply(Fixtures.WithBuilds(), new(ClickedChipRow: 0, ClickedChip: ChipKind.Cancel, Search: "inline"), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["Cancel jenkins/build-all/main"]);
        await Assert.That(state.Search).IsEqualTo("inline");
    }

    static int FailedRow(SessionState state) =>
        Fixtures.RowOf(state, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");

    static int RunningRow(SessionState state) =>
        Fixtures.RowOf(state, _ => _.Build?.Key == "gh/DiffEngine/test.yml/main");

    [Test]
    public async Task ClickingRetryAsksForARetry()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        var state = Apply(builds, new(ClickedChipRow: FailedRow(builds), ClickedChip: ChipKind.Retry), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["Retry gh/Verify/test.yml/feature/inline"]);
        await Assert.That(state.Status).IsEqualTo("Retrying test.yml #77");
    }

    [Test]
    public async Task RetryOnARunningBuildIsIgnored()
    {
        var actions = new RecordingActions();
        Apply(Fixtures.WithBuilds(), new(ClickedChipRow: 1, ClickedChip: ChipKind.Retry), actions);
        await Assert.That(actions.Calls).IsEmpty();
    }

    [Test]
    public async Task ClickingCopyLogAsksForTheLog()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        var state = Apply(builds, new(ClickedChipRow: FailedRow(builds), ClickedChip: ChipKind.CopyLog), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["CopyLog gh/Verify/test.yml/feature/inline"]);
        await Assert.That(state.Status).IsEqualTo("Fetching the log of test.yml #77");
    }

    [Test]
    public async Task CopyLogOnABuildThatDidNotFailIsIgnored()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        Apply(MonitorSession.SelectRow(builds, RunningRow(builds)), new(Key: CommandKind.CopyLog), actions);
        await Assert.That(actions.Calls).IsEmpty();
    }

    [Test]
    public async Task AChipOfARowThatIsGoneDoesNothing()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        var state = Apply(builds, new(ClickedChipRow: 40, ClickedChip: ChipKind.Cancel), actions);
        await Assert.That(actions.Calls).IsEmpty();
        await Assert.That(state.SelectedRow).IsEqualTo(builds.SelectedRow);
    }

    [Test]
    public async Task TheOverflowChipOpensTheChipsItStandsInFor()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        var row = FailedRow(builds);
        var state = Apply(builds, new(ClickedOverflowRow: row, OverflowFrom: ChipKind.Retry), actions);
        await Assert.That(state.Menu!.Overflow).IsTrue();
        await Assert.That(state.Menu.Items.Select(_ => _.Label)).IsEquivalentTo(["Retry", "Log"]);
        await Assert.That(state.SelectedRow).IsEqualTo(row);

        state = Apply(state, new(ClickedMenuItem: 1), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["CopyLog gh/Verify/test.yml/feature/inline"]);
        await Assert.That(state.Menu).IsNull();
    }

    [Test]
    public async Task CancelFromTheMenu()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        var state = Apply(builds, new(RightClickedRow: RunningRow(builds)), actions);
        var cancelIndex = state.Menu!.Items.ToList().FindIndex(_ => _.Command == CommandKind.Cancel);
        state = Apply(state, new(ClickedMenuItem: cancelIndex), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["Cancel gh/DiffEngine/test.yml/main"]);
        await Assert.That(state.Menu).IsNull();
    }

    [Test]
    public async Task CopyBuildUrlUsesTheWindowClipboard()
    {
        var window = new FakeWindow();
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.SelectRow(builds, RunningRow(builds));
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
    public async Task ProjectLinkOpensTheProjectPage()
    {
        var actions = new RecordingActions();
        var state = Fixtures.WithBuilds();
        state = state with { Builds = [..state.Builds.Select(_ => _ with { ProjectUrl = "https://example.com/project" })] };
        Apply(state, new(ClickedChipRow: 0, ClickedChip: ChipKind.Project), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["OpenUrl https://example.com/project"]);
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

    /// <summary>
    /// Run at startup is the one option set somewhere other than settings.json, so it is the one
    /// that can refuse. The rest are saved either way, and the status says which one did not take:
    /// a message the action swaps into the host instead is undone the moment this returns.
    /// </summary>
    [Test]
    public async Task StartupThatWillNotRegisterSaysSo()
    {
        var actions = new RecordingActions { RunAtLoginError = "Run at startup failed: denied" };
        var state = Apply(Fixtures.Options(), new(FieldChanges: [new(FormFields.RunAtStartup, "true")]), actions);
        state = Apply(state, new(ClickedButton: 0), actions);

        await Assert.That(state.Status).IsEqualTo("Run at startup failed: denied");
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
    public async Task EditRowClickEditsTheConnection()
    {
        var state = Apply(Fixtures.Options(), new(ClickedField: FormFields.Connection(Fixtures.Octopus.Id)), new());
        await Assert.That(state.Page).IsEqualTo(Page.Connection);
        await Assert.That(state.Form!.EditingConnectionId).IsEqualTo(Fixtures.Octopus.Id);
    }

    [Test]
    public async Task InputClearsTheStatus()
    {
        var state = MonitorSession.SetStatus(Fixtures.WithBuilds(), "Done");
        state = Apply(state, new(Key: CommandKind.NextRow), new());
        await Assert.That(state.Status).IsEqualTo("");
    }

    [Test]
    public async Task ClickingAGroupOpensAndClosesIt()
    {
        var green = Fixtures.WithGreenProject();
        var row = Fixtures.RowOf(green, _ => _.Kind == RowKind.Group);
        var opened = Apply(green, new(ClickedRow: row), new());
        await Assert.That(RowProjection.Rows(opened).Count(_ => _.Kind == RowKind.Member)).IsEqualTo(2);
        await Assert.That(MonitorSession.SelectedRow(opened)!.Kind).IsEqualTo(RowKind.Group);

        var closed = Apply(opened, new(ClickedRow: row), new());
        await Assert.That(RowProjection.Rows(closed).Any(_ => _.Kind == RowKind.Member)).IsFalse();
    }

    [Test]
    public async Task ClickingABuildDoesNotToggleItsGroup()
    {
        var state = MonitorSession.ToggleGroup(Fixtures.WithGreenProject(), Fixtures.VerifyPassing);
        var next = Apply(state, new(ClickedRow: Fixtures.RowOf(state, _ => _.Kind == RowKind.Member)), new());
        await Assert.That(RowProjection.Rows(next).Count(_ => _.Kind == RowKind.Member)).IsEqualTo(2);
    }

    [Test]
    public async Task EnterAndTheMenuOpenAGroup()
    {
        var green = Fixtures.WithGreenProject();
        var row = Fixtures.RowOf(green, _ => _.Kind == RowKind.Group);
        var entered = Apply(MonitorSession.SelectRow(green, row), new(Key: CommandKind.OpenBuild), new());
        await Assert.That(RowProjection.Rows(entered).Count(_ => _.Kind == RowKind.Member)).IsEqualTo(2);

        var menu = Apply(green, new(RightClickedRow: row), new());
        var expanded = Apply(menu, new(ClickedMenuItem: 0), new());
        await Assert.That(RowProjection.Rows(expanded).Count(_ => _.Kind == RowKind.Member)).IsEqualTo(2);
    }

    [Test]
    public async Task ClickingTheFolderChipOpensTheCheckout()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithLocalRepos();
        var row = RunningRow(builds);
        var state = Apply(builds, new(ClickedChipRow: row, ClickedChip: ChipKind.OpenDirectory), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["OpenDirectory /code/DiffEngine"]);
        await Assert.That(state.Status).IsEqualTo("Opened /code/DiffEngine");
    }

    /// <summary>
    /// The chip is only offered where the lookup hits, so a build with no checkout has none to
    /// click. Asking anyway, which a stale frame could, must still do nothing.
    /// </summary>
    [Test]
    public async Task ABuildWithNoCheckoutHasNoFolderChipAndOpensNothing()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithLocalRepos();
        var row = FailedRow(builds);
        var chips = MonitorSession.SelectedBuild(MonitorSession.SelectRow(builds, row))!;
        await Assert.That(RowChips.Of(chips, builds.LocalRepos).Select(_ => _.Kind)).DoesNotContain(ChipKind.OpenDirectory);

        var state = Apply(builds, new(ClickedChipRow: row, ClickedChip: ChipKind.OpenDirectory), actions);
        await Assert.That(actions.Calls).IsEmpty();
        await Assert.That(state.Status).IsEqualTo(builds.Status);
    }

    /// <summary>
    /// Jenkins reports a job name rather than a slug, so the only thing a checkout of it can match
    /// on is the folder's own name.
    /// </summary>
    [Test]
    public async Task ACheckoutMatchedByFolderNameOpensToo()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithLocalRepos();
        var row = Fixtures.RowOf(builds, _ => _.Build?.Key == "jenkins/build-all/main");
        Apply(builds, new(ClickedChipRow: row, ClickedChip: ChipKind.OpenDirectory), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["OpenDirectory /code/build-all"]);
    }

    [Test]
    public async Task BrowsingPutsTheChosenDirectoryInTheField()
    {
        var window = new FakeWindow { Picked = "/code" };
        var state = MonitorSession.OpenOptions(Fixtures.WithBuilds());
        state = InputApplier.Apply(state, new(ClickedField: FormFields.CodeDirectory), new RecordingActions().Actions, window);
        await Assert.That(state.Form!.Value(FormFields.CodeDirectory)).IsEqualTo("/code");
        await Assert.That(window.Calls).IsEquivalentTo(["PickDirectory "]);
    }

    [Test]
    public async Task CancellingTheChooserLeavesTheFieldAlone()
    {
        var window = new FakeWindow();
        var state = MonitorSession.OpenOptions(Fixtures.WithBuilds());
        state = MonitorSession.FieldChanged(state, FormFields.CodeDirectory, "/was/here");
        var after = InputApplier.Apply(state, new(ClickedField: FormFields.CodeDirectory), new RecordingActions().Actions, window);
        await Assert.That(after.Form!.Value(FormFields.CodeDirectory)).IsEqualTo("/was/here");
        await Assert.That(window.Calls).IsEquivalentTo(["PickDirectory /was/here"]);
    }

    /// <summary>
    /// Update asks rather than does. The tray disappears for the length of an update, so the one
    /// chance to say what that costs is before it starts.
    /// </summary>
    [Test]
    public async Task UpdateOpensThePageRatherThanUpdating()
    {
        var actions = new RecordingActions
        {
            Servers = new([new(21044, Fixtures.Now - TimeSpan.FromHours(2))], true)
        };
        var state = Apply(Fixtures.WithBuilds(), new(TrayItem: TrayMenu.Update), actions);

        await Assert.That(state.Page).IsEqualTo(Page.Update);
        await Assert.That(state.Form!.Servers.Running.Single().ProcessId).IsEqualTo(21044);
        await Assert.That(actions.Calls).IsEquivalentTo(["RunningServers"]);
    }

    /// <summary>
    /// Confirming both starts the update and exits, and the exit is in the state this returns. The
    /// update replaces the files of the tray that asked for it, so a tray still running when the
    /// shell stops waiting is one whose update fails; an exit the action swaps into the host
    /// instead is undone by this returning, and what that looks like is a button doing nothing.
    /// </summary>
    [Test]
    public async Task ConfirmingOnThePageUpdatesAndExits()
    {
        var actions = new RecordingActions();
        var state = MonitorSession.OpenUpdate(Fixtures.WithBuilds(), McpServers.None);
        var after = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.ConfirmUpdate)), actions);

        await Assert.That(actions.Calls).IsEquivalentTo(["Update"]);
        await Assert.That(after.Exit).IsTrue();
    }

    [Test]
    public async Task CancellingThePageUpdatesNothing()
    {
        var actions = new RecordingActions();
        var state = MonitorSession.OpenUpdate(Fixtures.WithBuilds(), McpServers.None);
        var after = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.CancelForm)), actions);

        await Assert.That(after.Page).IsEqualTo(Page.Builds);
        await Assert.That(actions.Calls).IsEmpty();
    }

    static int ButtonIndex(SessionState state, CommandKind command) =>
        ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == command);

    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());
}
