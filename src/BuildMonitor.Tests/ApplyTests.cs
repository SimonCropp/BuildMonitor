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
    [Arguments(ChipKind.Build, "https://example.com/gh/Verify/test.yml/77")]
    [Arguments(ChipKind.Branch, "https://github.com/VerifyTests/Verify/tree/feature/inline")]
    public async Task ClickingANameInTheRowOpensIt(ChipKind link, string url)
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        var row = FailedRow(builds);
        var state = Apply(builds, new(ClickedChipRow: row, ClickedChip: link), actions);
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

    static int QueuedRow(SessionState state) =>
        Fixtures.RowOf(state, _ => _.Build?.Key == "tc/Verify_Build/main");

    static int RunningTeamCityRow(SessionState state) =>
        Fixtures.RowOf(state, _ => _.Build?.Key == "tc/Verify_Package/main");

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
    public async Task ClickingRunNextAsksForTheMove()
    {
        var actions = new RecordingActions();
        var queued = Fixtures.WithQueuePriority();
        var state = Apply(queued, new(ClickedChipRow: QueuedRow(queued), ClickedChip: ChipKind.RunNext), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["RunNext tc/Verify_Build/main"]);
        // No run number: TeamCity numbers a build when it starts, so the one thing the status can
        // name is the pipeline.
        await Assert.That(state.Status).IsEqualTo("Moving Verify / Build to the front of the queue");
    }

    /// <summary>
    /// The build started between the frame being drawn and the click landing on it, which is the
    /// case the applier re-checks for rather than trusting the chip.
    /// </summary>
    [Test]
    public async Task RunNextOnABuildThatHasLeftTheQueueIsIgnored()
    {
        var actions = new RecordingActions();
        var queued = Fixtures.WithQueuePriority();
        Apply(queued, new(ClickedChipRow: RunningTeamCityRow(queued), ClickedChip: ChipKind.RunNext), actions);
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// Jenkins queues builds but has no call to reorder the queue, so its queued row offers nothing
    /// and a command aimed at it anyway is refused.
    /// </summary>
    [Test]
    public async Task RunNextOnAServiceThatCannotReorderIsIgnored()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithBuilds();
        var row = Fixtures.RowOf(builds, _ => _.Build?.Key == "jenkins/nightly/");
        Apply(builds, new(ClickedChipRow: row, ClickedChip: ChipKind.RunNext), actions);
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

    /// <summary>
    /// The whole way through: the menu offers the prefix the row's project shares with another,
    /// the click groups by it, and the settings it was written to are saved.
    /// </summary>
    [Test]
    public async Task GroupByPrefixFromTheMenu()
    {
        var actions = new RecordingActions();
        // The fixture's own prefix cleared, so the menu is the only thing that sets one.
        var builds = MonitorSession.ApplySettings(Fixtures.WithPrefixGroup(), Fixtures.Settings());
        var row = Fixtures.RowOf(builds, _ => _.Build?.RepoName == "VerifyTests/VerifyXunit");
        var state = Apply(builds, new(RightClickedRow: row), actions);
        var items = state.Menu!.Items.Where(_ => _.Command == CommandKind.GroupByPrefix).ToList();
        await Assert.That(items.Select(_ => _.Label)).IsEquivalentTo(["Group by prefix: Verify"]);

        state = Apply(state, new(ClickedMenuItem: state.Menu.Items.IndexOf(items[0])), actions);
        await Assert.That(state.Settings.GroupPrefixes).IsEquivalentTo(["Verify"]);
        await Assert.That(actions.Calls).IsEquivalentTo(["SaveSettings"]);
        // The repository's own two passing workflows and VerifyXunit's, which had a row of its own.
        await Assert.That(RowProjection.Rows(state).Single(_ => _.Kind == RowKind.Group).Members.Length).IsEqualTo(3);
    }

    /// <summary>
    /// And back off again from the group's own row, which is where the grouping is seen rather
    /// than on the options page.
    /// </summary>
    [Test]
    public async Task StopGroupingFromTheGroupsMenu()
    {
        var actions = new RecordingActions();
        var grouped = Fixtures.WithPrefixGroup();
        var row = Fixtures.RowOf(grouped, _ => _.Kind == RowKind.Group);
        var state = Apply(grouped, new(RightClickedRow: row), actions);
        var index = state.Menu!.Items.ToList().FindIndex(_ => _.Command == CommandKind.RemoveGroupPrefix);
        await Assert.That(state.Menu.Items[index].Label).IsEqualTo("Stop grouping: Verify");

        state = Apply(state, new(ClickedMenuItem: index), actions);
        await Assert.That(state.Settings.GroupPrefixes).IsEmpty();
        await Assert.That(actions.Calls).IsEquivalentTo(["SaveSettings"]);
        // Back to the row it had, the group left holding the repository's own workflows.
        await Assert.That(RowProjection.Rows(state).Any(_ => _.Kind == RowKind.Build && _.Build!.RepoName == "VerifyTests/VerifyXunit")).IsTrue();
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

    // The whole way through: the footer button the sign in page offers, resolved by the index a head
    // reports, reaching the transition that puts the device code back on the clipboard.
    [Test]
    public async Task ClickingCopyCodeCopiesTheDeviceCode()
    {
        var signedIn = Fixtures.SignInDevice();
        // As the loop leaves it, the arrival's copy already flushed, so only the click can be what
        // puts the code back.
        var state = MonitorSession.Copied(signedIn, signedIn.Clipboard!);
        var buttons = ScreenBuilder.Buttons(state);
        var copyIndex = buttons.ToList().FindIndex(_ => _.Command == CommandKind.CopyUserCode);
        await Assert.That(copyIndex).IsEqualTo(0);
        state = InputApplier.Apply(state, new(ClickedButton: copyIndex), new RecordingActions().Actions, new FakeWindow());
        await Assert.That(state.Clipboard?.Text).IsEqualTo("ABCD-1234");
        await Assert.That(state.Status).IsEqualTo("Copied the code");
        // Still on the page: copying a code is not leaving the sign in.
        await Assert.That(state.Page).IsEqualTo(Page.SignIn);
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

    /// <summary>
    /// The icon and the name are the two halves of what was one link, and they open two different
    /// pages: an AppVeyor logo used to open github.com because one field held both.
    /// </summary>
    [Test]
    public async Task TheIconOpensThePipelineAndTheNameTheRepository()
    {
        var actions = new RecordingActions();
        var state = Fixtures.WithBuilds();
        state = state with
        {
            Builds =
            [
                .. state.Builds.Select(_ => _ with
                {
                    PipelineUrl = "https://ci.example.com/pipeline",
                    RepoUrl = "https://github.com/owner/name"
                })
            ]
        };
        Apply(state, new(ClickedChipRow: 0, ClickedChip: ChipKind.Pipeline), actions);
        Apply(state, new(ClickedChipRow: 0, ClickedChip: ChipKind.Repo), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(
        [
            "OpenUrl https://ci.example.com/pipeline",
            "OpenUrl https://github.com/owner/name"
        ]);
    }

    /// <summary>
    /// A member's own first cell is blank, so the group's row is the only place its repository is
    /// named, and it resolves through the members the way the shared folder chip does.
    /// </summary>
    [Test]
    public async Task AGroupOpensTheRepositoryItsMembersShare()
    {
        var actions = new RecordingActions();
        var state = Fixtures.WithGreenProject();
        var group = RowProjection.Rows(state).Select((row, index) => (row, index)).First(_ => _.row.Kind == RowKind.Group);
        Apply(state, new(ClickedChipRow: group.index, ClickedChip: ChipKind.Repo), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["OpenUrl https://github.com/VerifyTests/Verify"]);
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
        var actions = new RecordingActions
        {
            RunAtLoginError = "Run at startup failed: denied"
        };
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
        await Assert.That(state.Form!.Error).IsEqualTo(new("The poll interval must be between 5 and 3600 seconds.", FormFields.PollInterval));
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

    /// <summary>
    /// Remove sits beside Cancel, and used to take the connection and its credential on one click.
    /// </summary>
    [Test]
    public async Task RemoveAsksFirst()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.ConnectionEdit(), new(ClickedButton: ButtonIndex(Fixtures.ConnectionEdit(), CommandKind.RemoveConnection)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.RemoveConnection);
        await Assert.That(state.Settings.Connections.Any(_ => _.Id == Fixtures.Jenkins.Id)).IsTrue();
        await Assert.That(actions.Calls).IsEmpty();
    }

    [Test]
    public async Task RemovingAConnectionDeletesItsSecrets()
    {
        var actions = new RecordingActions();
        var state = MonitorSession.OpenRemoveConnection(Fixtures.ConnectionEdit());
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.ConfirmRemoveConnection)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
        await Assert.That(state.Status).IsEqualTo("Removed Jenkins");
        await Assert.That(state.Settings.Connections.Any(_ => _.Id == Fixtures.Jenkins.Id)).IsFalse();
        await Assert.That(actions.Calls).IsEquivalentTo(["DeleteSecret connection:jenkins", "DeleteSecret connection:jenkins:refresh", "SaveSettings"]);
    }

    /// <summary>
    /// A no to "are you sure" goes back to the editor, not to the builds page, and keeps whatever
    /// was typed in it before Remove was clicked.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CancellingARemoveGoesBackToTheEditor(bool escape)
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.ConnectionEdit(), new(FieldChanges: [new(FormFields.Name, "Build server")]), actions);
        state = MonitorSession.OpenRemoveConnection(state);
        MonitorInput cancel = escape
            ? new(Key: CommandKind.CancelForm)
            : new(ClickedButton: ButtonIndex(state, CommandKind.CancelForm));
        state = Apply(state, cancel, actions);
        await Assert.That(state.Page).IsEqualTo(Page.EditConnection);
        await Assert.That(Fixtures.ConnectionForm(state).Value(FormFields.Name)).IsEqualTo("Build server");
        await Assert.That(state.Settings.Connections.Any(_ => _.Id == Fixtures.Jenkins.Id)).IsTrue();
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// The delete is reachable only from the page that asked about it.
    /// </summary>
    [Test]
    public async Task ConfirmingFromTheEditorDoesNothing()
    {
        var actions = new RecordingActions();
        var state = Fixtures.ConnectionEdit();
        var next = Apply(state, new(Key: CommandKind.ConfirmRemoveConnection), actions);
        await Assert.That(next.Page).IsEqualTo(Page.EditConnection);
        await Assert.That(next.Settings.Connections.Length).IsEqualTo(state.Settings.Connections.Length);
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// A yes to a connection that went while the page was open removes nothing and deletes no
    /// credential, and leaves a page asking about nothing.
    /// </summary>
    [Test]
    public async Task ConfirmingAConnectionAlreadyGoneDeletesNothing()
    {
        var actions = new RecordingActions();
        var state = MonitorSession.OpenRemoveConnection(Fixtures.ConnectionEdit());
        state = MonitorSession.RemoveConnection(state, Fixtures.Jenkins.Id);
        state = Apply(state, new(Key: CommandKind.ConfirmRemoveConnection), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// What adding it back costs depends on how it signed in.
    /// </summary>
    [Test]
    [Arguments(AuthMethod.Token, "Adding it back means entering a token again.")]
    [Arguments(AuthMethod.Device, "Adding it back means signing in again.")]
    public async Task TheRemovePageSaysWhatComingBackCosts(AuthMethod method, string expected)
    {
        var state = MonitorSession.ReplaceConnection(
            Fixtures.WithBuilds(),
            Fixtures.GitHub
                with
                {
                    Auth = method
                });
        state = MonitorSession.OpenRemoveConnection(MonitorSession.OpenEditConnection(state, Fixtures.GitHub.Id));
        var fields = ScreenBuilder.Build(state, Fixtures.Now).Form!.Fields;
        await Assert.That(fields.Single(_ => _.Id == FormFields.RemoveReturn).Value).IsEqualTo(expected);
    }

    /// <summary>
    /// The command is only reachable from the edit page, but it used to work off whichever id the
    /// form happened to hold, so a new connection's draft id would have been removed and its
    /// secrets deleted.
    /// </summary>
    [Test]
    public async Task RemovingFromTheAddPageDoesNothing()
    {
        var actions = new RecordingActions();
        var state = Fixtures.ConnectionNew();
        var next = Apply(state, new(Key: CommandKind.RemoveConnection), actions);
        await Assert.That(next.Settings.Connections.Length).IsEqualTo(state.Settings.Connections.Length);
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// An edited connection's token is the connection's own, not a draft's, so leaving the editor
    /// must not delete it.
    /// </summary>
    [Test]
    public async Task CancellingAnEditDeletesNoSecret()
    {
        var actions = new RecordingActions();
        var state = Apply(Fixtures.ConnectionEdit(), new(Key: CommandKind.CancelForm), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
        await Assert.That(actions.Calls).IsEmpty();
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
        await Assert.That(state.Page).IsEqualTo(Page.EditConnection);
        await Assert.That(Fixtures.ConnectionForm(state).ConnectionId).IsEqualTo(Fixtures.Octopus.Id);
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
    /// A closed group hides its members' chips, and its own row can stand in for the folder one
    /// while every member is the same checkout.
    /// </summary>
    [Test]
    public async Task AGroupWhoseMembersShareACheckoutOffersTheFolderChip()
    {
        var actions = new RecordingActions();
        var green = MonitorSession.ApplyLocalRepos(
            Fixtures.WithGreenProject(),
            LocalRepos.Index([new("/code/Verify", "Verify", "VerifyTests/Verify")]));
        var row = Fixtures.RowOf(green, _ => _.Kind == RowKind.Group);
        var chips = ScreenBuilder.Build(green, Fixtures.Now).Builds!.Rows[row].Chips;
        await Assert.That(chips.Select(_ => _.Kind)).Contains(ChipKind.OpenDirectory);

        var state = Apply(green, new(ClickedChipRow: row, ClickedChip: ChipKind.OpenDirectory), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["OpenDirectory /code/Verify"]);
        await Assert.That(state.Status).IsEqualTo("Opened /code/Verify");
    }

    [Test]
    public async Task AGroupWithNoCheckoutHasNoFolderChip()
    {
        var green = MonitorSession.ApplyLocalRepos(Fixtures.WithGreenProject(), Fixtures.LocalRepoIndex());
        var row = Fixtures.RowOf(green, _ => _.Kind == RowKind.Group);
        var chips = ScreenBuilder.Build(green, Fixtures.Now).Builds!.Rows[row].Chips;
        await Assert.That(chips.Select(_ => _.Kind)).DoesNotContain(ChipKind.OpenDirectory);
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
        await Assert.That(RowChips.Of(chips, ProviderDescriptors.Get(Fixtures.GitHub.ProviderId), builds.LocalRepos, false).Select(_ => _.Kind)).DoesNotContain(ChipKind.OpenDirectory);

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
    public async Task ClickingTheTriageChipCollectsTheBuild()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithTriageableFailure();
        var row = FailedRow(builds);
        var state = Apply(builds, new(ClickedChipRow: row, ClickedChip: ChipKind.Triage), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["Triage gh/Verify/test.yml/feature/inline"]);
        await Assert.That(MonitorSession.IsTriaging(state, MonitorSession.SelectedBuild(state)!)).IsTrue();
        await Assert.That(ScreenBuilder.Status(state, Fixtures.Now)).IsEqualTo("Collecting test.yml #77 for triage");
    }

    /// <summary>
    /// A download slow enough to be clicked again is the one this state exists for, and the second
    /// click would fetch the whole bundle again only to copy the same prompt twice.
    /// </summary>
    [Test]
    public async Task ASecondTriageClickWhileCollectingDoesNothing()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithTriageableFailure();
        var click = new MonitorInput(ClickedChipRow: FailedRow(builds), ClickedChip: ChipKind.Triage);
        var state = Apply(Apply(builds, click, actions), click, actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["Triage gh/Verify/test.yml/feature/inline"]);
        await Assert.That(state.Triaging.Length).IsEqualTo(1);
    }

    /// <summary>
    /// The bug this state was added for: any input clears the status line, and the click's own
    /// "Collecting" was the only thing saying the prompt was not on the clipboard yet.
    /// </summary>
    [Test]
    public async Task TheFooterStillSaysCollectingAfterTheNextClick()
    {
        var builds = Fixtures.WithTriageableFailure();
        var state = Apply(builds, new(ClickedChipRow: FailedRow(builds), ClickedChip: ChipKind.Triage), new());
        state = Apply(state, new(ClickedRow: RunningRow(state)), new());
        await Assert.That(state.Status).IsEqualTo("");
        await Assert.That(ScreenBuilder.Status(state, Fixtures.Now)).IsEqualTo("Collecting test.yml #77 for triage");
    }

    /// <summary>
    /// The menu is another way to the same click, so it must not offer the download the chip
    /// beside it already shows is on its way.
    /// </summary>
    [Test]
    public async Task TheMenuNamesATriageThatIsCollecting()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.Triaging();
        var menu = Apply(builds, new(RightClickedRow: FailedRow(builds)), actions);
        var index = menu.Menu!.Items.ToList().FindIndex(_ => _.Command == CommandKind.Triage);
        await Assert.That(menu.Menu.Items[index].Label).IsEqualTo("Triaging");

        Apply(menu, new(ClickedMenuItem: index), actions);
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// Both halves of the gate, because each on its own leaves the prompt with nothing to say: no
    /// checkout and there is nowhere to send an assistant, no failure and there is nothing to read.
    /// </summary>
    [Test]
    public async Task ThereIsNoTriageChipWithoutBothAFailureAndACheckout()
    {
        var withCheckout = Fixtures.WithTriageableFailure();
        var green = MonitorSession.SelectedBuild(MonitorSession.SelectRow(withCheckout, RunningRow(withCheckout)))!;
        await Assert.That(RowChips.Of(green, ProviderDescriptors.Get(Fixtures.GitHub.ProviderId), withCheckout.LocalRepos, false).Select(_ => _.Kind)).DoesNotContain(ChipKind.Triage);

        var noCheckout = Fixtures.WithLocalRepos();
        var failed = MonitorSession.SelectedBuild(MonitorSession.SelectRow(noCheckout, FailedRow(noCheckout)))!;
        await Assert.That(RowChips.Of(failed, ProviderDescriptors.Get(Fixtures.GitHub.ProviderId), noCheckout.LocalRepos, false).Select(_ => _.Kind)).DoesNotContain(ChipKind.Triage);
    }

    /// <summary>
    /// A poll between the frame being drawn and the click landing can have finished the build
    /// green or taken the checkout away, so the applier checks again rather than trusting the chip.
    /// </summary>
    [Test]
    public async Task AStaleTriageClickOnARowWithNoCheckoutDoesNothing()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithLocalRepos();
        var state = Apply(builds, new(ClickedChipRow: FailedRow(builds), ClickedChip: ChipKind.Triage), actions);
        await Assert.That(actions.Calls).IsEmpty();
        await Assert.That(state.Status).IsEqualTo(builds.Status);
    }

    /// <summary>
    /// A group's row stands for its members, so it cannot say whose artifacts to download. The
    /// applier already refuses every chip but the folder on a group row; this pins that it covers
    /// the triage chip too.
    /// </summary>
    [Test]
    public async Task AGroupRowRefusesATriageClick()
    {
        var actions = new RecordingActions();
        var grouped = MonitorSession.ApplyLocalRepos(
            Fixtures.WithGreenProject(),
            LocalRepos.Index([new("/code/Verify", "Verify", "VerifyTests/Verify")]));
        var row = Fixtures.RowOf(grouped, _ => _.Kind == RowKind.Group);
        var chips = ScreenBuilder.Build(grouped, Fixtures.Now).Builds!.Rows[row].Chips;
        await Assert.That(chips.Select(_ => _.Kind)).DoesNotContain(ChipKind.Triage);

        Apply(grouped, new(ClickedChipRow: row, ClickedChip: ChipKind.Triage), actions);
        await Assert.That(actions.Calls).IsEmpty();
    }

    [Test]
    public async Task BrowsingPutsTheChosenDirectoryInTheField()
    {
        var window = new FakeWindow
        {
            Picked = "/code"
        };
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
        await Assert.That(((UpdateFormState) state.Form!).Servers.Running.Single().ProcessId).IsEqualTo(21044);
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

    /// <summary>
    /// Where the window settled is saved for the next start, and only when it moved: the head
    /// reports every hide, and a hide from where it already was is not worth a write.
    /// </summary>
    [Test]
    public async Task AWindowPlacementIsSavedWhenItChanges()
    {
        var actions = new RecordingActions();
        var placement = new WindowPlacement(100, 80, 1200, 800);
        var state = Apply(Fixtures.WithBuilds(), new(Placement: placement), actions);
        await Assert.That(actions.SavedSettings!.Window).IsEqualTo(placement);

        actions.Calls.Clear();
        Apply(state, new(Placement: new(100, 80, 1200, 800)), actions);
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// Moving the window is not reading the status line, so the message stays.
    /// </summary>
    [Test]
    public async Task AWindowPlacementLeavesTheStatus()
    {
        var state = MonitorSession.SetStatus(Fixtures.WithBuilds(), "Saved GitHub");
        state = Apply(state, new(Placement: new(100, 80, 1200, 800, Maximized: true)), new());
        await Assert.That(state.Status).IsEqualTo("Saved GitHub");
    }

    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());
}
