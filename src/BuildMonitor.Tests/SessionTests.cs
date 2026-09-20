public class SessionTests
{
    [Test]
    public Task RowsAreOneListAcrossConnections() =>
        Verify(RowProjection.Rows(Fixtures.WithBuilds()).Select(_ => $"{_.Kind} {_.Connection?.Connection.Name} {_.Build?.PipelineName} {_.Build?.Branch} {_.Build?.Status}"))
            .Snapshot(
                """
                [
                  Build Jenkins Build all main Running,
                  Build Octopus Deploy Web  Running,
                  Build GitHub test.yml main Running,
                  Build Jenkins Nightly  Queued,
                  Build GitHub test.yml feature/inline Failed,
                  Build GitHub docs.yml main Succeeded
                ]
                """);

    [Test]
    public async Task GreenBuildsOfOneProjectShareAClosedGroup()
    {
        var rows = RowProjection.Rows(Fixtures.WithGreenProject());
        var group = rows.Single(_ => _.Kind == RowKind.Group);
        await Assert.That(group.Group).IsEqualTo(Fixtures.VerifyPassing);
        await Assert.That(group.Expanded).IsFalse();
        await Assert.That(string.Join(',', group.Members.Select(_ => _.PipelineName))).IsEqualTo("docs.yml,nuget.yml");
        await Assert.That(rows.Any(_ => _.Kind == RowKind.Member)).IsFalse();
        // The failing workflow of the same project is not hidden in the green group.
        await Assert.That(rows.Count(_ => _.Build is { RepoName: "VerifyTests/Verify", Status: BuildStatus.Failed })).IsEqualTo(1);
        // A project with one green workflow has nothing to group with.
        await Assert.That(rows.Count(_ => _.Build is { RepoName: "VerifyTests/DiffEngine", PipelineName: "docs.yml" })).IsEqualTo(1);
    }

    /// <summary>
    /// Two failures of one project are two rows, not a group that says "2 failing" and hides which
    /// pipeline broke. The project's passing workflows are still one group.
    /// </summary>
    [Test]
    public async Task FailedBuildsOfOneProjectEachKeepTheirRow()
    {
        var rows = RowProjection.Rows(Fixtures.WithTwoFailures());
        var failures = rows.Where(_ => _.Build is { RepoName: "VerifyTests/Verify", Status: BuildStatus.Failed }).ToList();
        await Assert.That(failures.Select(_ => _.Build!.PipelineName).Order()).IsEquivalentTo(["release.yml", "test.yml"]);
        await Assert.That(failures.All(_ => _.Kind == RowKind.Build)).IsTrue();
        await Assert.That(rows.Count(_ => _.Kind == RowKind.Group)).IsEqualTo(1);
    }

    [Test]
    public async Task GroupsSpanConnections()
    {
        var state = Fixtures.WithGreenProject();
        var job = Fixtures.Build(Fixtures.Jenkins.Id, "verify", "verify", "Verify", "main", "9", BuildStatus.Succeeded, started: Fixtures.Now - TimeSpan.FromHours(2), finished: Fixtures.Now - TimeSpan.FromHours(2) + TimeSpan.FromMinutes(3));
        var next = MonitorSession.ApplyPoll(state, Fixtures.Jenkins.Id, [], [..Fixtures.JenkinsBuilds(), job], Fixtures.Now);
        var passing = RowProjection.Rows(next).Single(_ => _.Group == Fixtures.VerifyPassing && _.Kind == RowKind.Group);
        await Assert.That(passing.Members.Select(_ => _.ConnectionId).Distinct().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task SingleGreenBuildIsNotGrouped() =>
        await Assert.That(RowProjection.Rows(Fixtures.WithBuilds()).Any(_ => _.Kind == RowKind.Group)).IsFalse();

    [Test]
    public async Task ToggleGroupTwiceRestores()
    {
        var state = Fixtures.WithGreenProject();
        var once = MonitorSession.ToggleGroup(state, Fixtures.VerifyPassing);
        var twice = MonitorSession.ToggleGroup(once, Fixtures.VerifyPassing);
        await Assert.That(RowProjection.Rows(once).Count(_ => _.Kind == RowKind.Member)).IsEqualTo(2);
        await Assert.That(twice.OpenGroups).IsEmpty();
        await Assert.That(RowProjection.Rows(twice).Length).IsEqualTo(RowProjection.Rows(state).Length);
    }

    [Test]
    public async Task ToggleGroupKeepsSelectionOnTheGroup()
    {
        var green = Fixtures.WithGreenProject();
        var state = MonitorSession.SelectRow(green, Fixtures.RowOf(green, _ => _.Kind == RowKind.Group));
        var expanded = MonitorSession.ToggleGroup(state, Fixtures.VerifyPassing);
        await Assert.That(MonitorSession.SelectedRow(expanded)!.Kind).IsEqualTo(RowKind.Group);

        var member = MonitorSession.SelectRow(expanded, expanded.SelectedRow + 1);
        await Assert.That(MonitorSession.SelectedRow(member)!.Kind).IsEqualTo(RowKind.Member);

        var collapsed = MonitorSession.ToggleGroup(member, Fixtures.VerifyPassing);
        await Assert.That(MonitorSession.SelectedRow(collapsed)!.Kind).IsEqualTo(RowKind.Group);
    }

    /// <summary>
    /// The group it leaves holds the one workflow still passing, and a group of one is no group,
    /// so both of them end up on rows of their own.
    /// </summary>
    [Test]
    public async Task AGreenBuildThatFailsLeavesItsGreenGroup()
    {
        var state = Fixtures.WithGreenProject();
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], WithStatus(state, "Verify/nuget.yml", BuildStatus.Failed), Fixtures.Now);
        var rows = RowProjection.Rows(next);
        await Assert.That(rows.Any(_ => _.Group == Fixtures.VerifyPassing)).IsFalse();
        var failed = rows.Single(_ => _.Build is { PipelineName: "nuget.yml" });
        await Assert.That(failed.Kind).IsEqualTo(RowKind.Build);
        await Assert.That(failed.Build!.Status).IsEqualTo(BuildStatus.Failed);
    }

    [Test]
    public async Task OpenBuildOnGroupRowTogglesIt()
    {
        var green = Fixtures.WithGreenProject();
        var state = MonitorSession.SelectRow(green, Fixtures.RowOf(green, _ => _.Kind == RowKind.Group));
        var next = InputApplier.Execute(state, CommandKind.OpenBuild, null, MonitorActions.None, null);
        await Assert.That(next.OpenGroups).Contains(Fixtures.VerifyPassing.Id);
    }

    [Test]
    public async Task ScrollClampsToRows()
    {
        var state = MonitorSession.Resize(Fixtures.WithBuilds(), 120, 12);
        var total = RowProjection.Rows(state).Length;
        var body = MonitorSession.BodyRows(state);

        var scrolled = MonitorSession.Scroll(state, 100);
        await Assert.That(scrolled.ScrollTop).IsEqualTo(total - body);

        var back = MonitorSession.Scroll(scrolled, -100);
        await Assert.That(back.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task SelectionScrollsIntoView()
    {
        var state = MonitorSession.Resize(Fixtures.WithBuilds(), 120, 10);
        var body = MonitorSession.BodyRows(state);

        var last = MonitorSession.SelectRow(state, 5);
        await Assert.That(last.SelectedRow).IsEqualTo(5);
        await Assert.That(last.ScrollTop).IsEqualTo(5 - body + 1);

        var first = MonitorSession.SelectRow(last, 0);
        await Assert.That(first.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task SelectionSurvivesFewerRows()
    {
        var state = MonitorSession.SelectRow(Fixtures.WithBuilds(), 5);
        var fewer = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [], Fixtures.Now);
        var total = RowProjection.Rows(fewer).Length;
        await Assert.That(fewer.SelectedRow).IsLessThan(total);
    }

    [Test]
    public async Task SelectionFollowsItsPipelineToAnotherBranch()
    {
        // A run of docs.yml starting on a branch takes over the pipeline's row and, being running,
        // sorts to the top.
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.SelectRow(builds, DocsRow(builds));
        var run = Fixtures.Build(Fixtures.GitHub.Id, "DiffEngine/docs.yml", "docs.yml", "VerifyTests/DiffEngine", "feature/x", "301", BuildStatus.Running, started: Fixtures.Now);
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [..Fixtures.GitHubBuilds(), run], Fixtures.Now);
        await Assert.That(MonitorSession.SelectedBuild(next)?.Key).IsEqualTo("gh/DiffEngine/docs.yml/feature/x");
    }

    [Test]
    public async Task SelectionMovesUpWhenItsPipelineIsExcluded()
    {
        // docs.yml is the last row, under Verify's failure.
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.SelectRow(builds, DocsRow(builds));
        var next = MonitorSession.ExcludePipeline(state, MonitorSession.SelectedBuild(state)!);
        await Assert.That(MonitorSession.SelectedBuild(next)?.Key).IsEqualTo("gh/Verify/test.yml/feature/inline");
    }

    static int DocsRow(SessionState state) =>
        Fixtures.RowOf(state, _ => _.Build?.Key == "gh/DiffEngine/docs.yml/main");

    [Test]
    [Arguments("diffengine", "gh/DiffEngine/test.yml/main,gh/DiffEngine/docs.yml/main")]
    [Arguments("DOCS", "gh/DiffEngine/docs.yml/main")]
    [Arguments("inline", "gh/Verify/test.yml/feature/inline")]
    [Arguments(" nightly ", "jenkins/nightly/")]
    // The owner is not the project a row shows, so it matches nothing.
    [Arguments("VerifyTests", "")]
    public async Task SearchMatchesTheProjectThePipelineOrTheBranch(string search, string keys)
    {
        var state = MonitorSession.Search(Fixtures.WithBuilds(), search);
        await Assert.That(string.Join(',', RowProjection.Rows(state).Select(_ => _.Build?.Key))).IsEqualTo(keys);
    }

    [Test]
    public async Task SearchLiftsAMatchOutOfItsClosedGroup()
    {
        // Verify's green docs.yml and nuget.yml share a closed group, and only nuget.yml matches.
        var row = RowProjection.Rows(MonitorSession.Search(Fixtures.WithGreenProject(), "nuget")).Single();
        await Assert.That(row.Kind).IsEqualTo(RowKind.Build);
        await Assert.That(row.Build!.PipelineName).IsEqualTo("nuget.yml");
    }

    [Test]
    public async Task SearchForAProjectKeepsItsGroup()
    {
        var rows = RowProjection.Rows(MonitorSession.Search(Fixtures.WithGreenProject(), "verify"));
        await Assert.That(rows.Select(_ => _.Kind)).IsEquivalentTo([RowKind.Build, RowKind.Group]);
    }

    [Test]
    public async Task SearchLeavesTheTrayAndTheCountsAlone()
    {
        var builds = Fixtures.WithBuilds();
        var searched = MonitorSession.Search(builds, "docs");
        await Assert.That(ScreenBuilder.Tray(searched).Icon).IsEqualTo(TrayIconKind.Failed);
        await Assert.That(ScreenBuilder.Tray(searched).Tooltip).IsEqualTo(ScreenBuilder.Tray(builds).Tooltip);
        await Assert.That(ScreenBuilder.Build(searched, Fixtures.Now).Builds!.Header).IsEqualTo("6 pipelines, 1 failing, 4 running");
    }

    [Test]
    public async Task SearchKeepsTheSelectionOnItsBuild()
    {
        var builds = Fixtures.WithBuilds();
        var searched = MonitorSession.Search(MonitorSession.SelectRow(builds, DocsRow(builds)), "main");
        await Assert.That(MonitorSession.SelectedBuild(searched)?.Key).IsEqualTo("gh/DiffEngine/docs.yml/main");

        var cleared = MonitorSession.Search(searched, "");
        await Assert.That(MonitorSession.SelectedBuild(cleared)?.Key).IsEqualTo("gh/DiffEngine/docs.yml/main");
    }

    [Test]
    public async Task SearchScrollsTheSelectionIntoView()
    {
        // Nine rows in a body of six, scrolled to the last. Two docs.yml rows are left, both in view.
        var state = MonitorSession.Search(Fixtures.Scrolled(), "docs");
        await Assert.That(state.ScrollTop).IsEqualTo(0);
        await Assert.That(MonitorSession.SelectedBuild(state)?.Key).IsEqualTo("gh/DiffEngine/docs.yml/main");
    }

    [Test]
    public async Task MenuClosesWhenAPollMovesItsRow()
    {
        // The running DiffEngine build offers Cancel. A newer run sorts above it, so a menu left
        // where it was drawn would sit on another build.
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.OpenMenu(builds, Fixtures.RowOf(builds, _ => _.Build?.Key == "gh/DiffEngine/test.yml/main"));
        var rerun = Fixtures.Build(Fixtures.GitHub.Id, "Verify/test.yml", "test.yml", "VerifyTests/Verify", "feature/inline", "78", BuildStatus.Running, started: Fixtures.Now);
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [..Fixtures.GitHubBuilds(), rerun], Fixtures.Now);
        await Assert.That(next.Menu).IsNull();
        await Assert.That(MonitorSession.SelectedBuild(next)?.Key).IsEqualTo("gh/DiffEngine/test.yml/main");
    }

    [Test]
    public async Task MenuStaysWhenAPollLeavesItsRow()
    {
        var state = MonitorSession.OpenMenu(Fixtures.WithBuilds(), 1);
        var next = MonitorSession.ApplyPoll(state, Fixtures.Octopus.Id, [], Fixtures.OctopusBuilds(), Fixtures.Now - TimeSpan.FromSeconds(5));
        await Assert.That(next.Menu).IsEqualTo(state.Menu);
    }

    [Test]
    public async Task PollKeepsTheSelectionOnItsGroup()
    {
        // With DiffEngine's test.yml green too there are two green groups, DiffEngine's and
        // Verify's, which must stay two.
        var green = Fixtures.WithGreenProject();
        var builds = WithStatus(green, "DiffEngine/test.yml", BuildStatus.Succeeded);
        var polled = MonitorSession.ApplyPoll(green, Fixtures.GitHub.Id, [], builds, Fixtures.Now);
        var state = MonitorSession.SelectRow(polled, Fixtures.RowOf(polled, _ => _.Kind == RowKind.Group && _.Group == Fixtures.VerifyPassing));

        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], builds, Fixtures.Now);
        await Assert.That(MonitorSession.SelectedRow(next)!.Group).IsEqualTo(Fixtures.VerifyPassing);
        await Assert.That(RowProjection.Rows(next).Count(_ => _.Kind == RowKind.Group)).IsEqualTo(2);
    }

    [Test]
    public async Task SelectionFollowsABuildIntoItsGroup()
    {
        // DiffEngine's running test.yml, once it passes, joins docs.yml in a closed group.
        var green = Fixtures.WithGreenProject();
        var state = MonitorSession.SelectRow(green, Fixtures.RowOf(green, _ => _.Build?.Key == "gh/DiffEngine/test.yml/main"));
        var builds = WithStatus(state, "DiffEngine/test.yml", BuildStatus.Succeeded);
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], builds, Fixtures.Now);
        var selected = MonitorSession.SelectedRow(next)!;
        await Assert.That(selected.Kind).IsEqualTo(RowKind.Group);
        await Assert.That(selected.Group).IsEqualTo(new("DiffEngine"));
    }

    [Test]
    public async Task SelectionFollowsAGroupThatSplits()
    {
        // A failing nuget.yml leaves docs.yml on a row of its own.
        var green = Fixtures.WithGreenProject();
        var state = MonitorSession.SelectRow(green, Fixtures.RowOf(green, _ => _.Kind == RowKind.Group));
        var builds = WithStatus(state, "Verify/nuget.yml", BuildStatus.Failed);
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], builds, Fixtures.Now);
        await Assert.That(MonitorSession.SelectedBuild(next)?.Key).IsEqualTo("gh/Verify/docs.yml/main");
    }

    static ImmutableArray<Build> WithStatus(SessionState state, string pipelineId, BuildStatus status) =>
    [
        ..state.Builds
            .Where(_ => _.ConnectionId == Fixtures.GitHub.Id)
            .Select(_ => _.PipelineId == pipelineId ? _ with { Status = status } : _)
    ];

    [Test]
    public async Task MenuChoiceReturnsCommandAndCloses()
    {
        var state = Fixtures.WithMenu();
        var (next, command) = MonitorSession.ChooseMenuItem(state, 0);
        await Assert.That(command).IsEqualTo(CommandKind.OpenBuild);
        await Assert.That(next.Menu).IsNull();
    }

    [Test]
    public async Task MenuOpensOnTheRowItWasAskedFor()
    {
        var state = MonitorSession.OpenMenu(Fixtures.WithBuilds(), 3);
        await Assert.That(state.SelectedRow).IsEqualTo(3);
        await Assert.That(state.Menu!.Row).IsEqualTo(3);
    }

    [Test]
    public async Task AFailedBuildsMenuOffersItsLog()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.OpenMenu(builds, Fixtures.RowOf(builds, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline"));
        await Assert.That(state.Menu!.Items.Any(_ => _.Command == CommandKind.CopyLog)).IsTrue();
        await Assert.That(state.Menu.Overflow).IsFalse();
    }

    [Test]
    public async Task TheOverflowOffersEveryChipFromTheFirstHidden()
    {
        var builds = Fixtures.WithBuilds();
        var row = Fixtures.RowOf(builds, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");
        var state = MonitorSession.OpenOverflow(builds, row, ChipKind.PullRequest);
        await Assert.That(state.Menu!.Items.Select(_ => $"{_.Label} {_.Command}")).IsEquivalentTo(["PR 42 OpenPullRequest", "Retry Retry", "Log CopyLog"]);
        await Assert.That(state.Menu.Overflow).IsTrue();
        await Assert.That(state.Menu.Row).IsEqualTo(row);
    }

    /// <summary>
    /// A failing row with a checkout carries five chips, more than the column reserves room for, so
    /// triage is reached through the drop down. It is last in the chip order on purpose, being the
    /// most expensive action on the row, and this pins that losing the column still offers it.
    /// </summary>
    [Test]
    public async Task TheOverflowOffersTriage()
    {
        var builds = Fixtures.WithTriageableFailure();
        var row = Fixtures.RowOf(builds, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");
        var state = MonitorSession.OpenOverflow(builds, row, ChipKind.CopyLog);
        await Assert.That(state.Menu!.Items.Select(_ => $"{_.Label} {_.Command}"))
            .IsEquivalentTo(["Log CopyLog", "Open dir OpenRepoDirectory", "Triage Triage"]);
    }

    /// <summary>
    /// The two rows of a service whose queue can be reordered: only the one still waiting offers
    /// the move, since a build already running has left the queue.
    /// </summary>
    [Test]
    public async Task OnlyAQueuedRowOffersRunNext()
    {
        var state = Fixtures.WithQueuePriority();
        var queued = MonitorSession.OpenMenu(state, Fixtures.RowOf(state, _ => _.Build?.PipelineId == "Verify_Build"));
        await Assert.That(queued.Menu!.Items.Any(_ => _.Command == CommandKind.RunNext)).IsTrue();

        var running = MonitorSession.OpenMenu(state, Fixtures.RowOf(state, _ => _.Build?.PipelineId == "Verify_Package"));
        await Assert.That(running.Menu!.Items.Any(_ => _.Command == CommandKind.RunNext)).IsFalse();
    }

    /// <summary>
    /// Jenkins queues builds too, and has no call to reorder its queue. Its queued row offers Cancel
    /// and nothing else, rather than a button that would be refused after the click.
    /// </summary>
    [Test]
    public async Task AQueuedRowOnAServiceThatCannotReorderOffersNoRunNext()
    {
        var builds = Fixtures.WithBuilds();
        var state = MonitorSession.OpenMenu(builds, Fixtures.RowOf(builds, _ => _.Build?.PipelineId == "nightly"));
        await Assert.That(state.Menu!.Items.Any(_ => _.Command == CommandKind.Cancel)).IsTrue();
        await Assert.That(state.Menu.Items.Any(_ => _.Command == CommandKind.RunNext)).IsFalse();
    }

    /// <summary>
    /// A connection found able only to watch loses Run next with Cancel, through the one flag both
    /// read: the right to reorder a queue is the right to change what is about to run.
    /// </summary>
    [Test]
    public async Task AWatchOnlyConnectionOffersNoRunNext()
    {
        var state = Fixtures.WithQueuePriority();
        var watching = state.Builds.Select(_ => _.WatchOnly());
        state = MonitorSession.ApplyPoll(state, Fixtures.TeamCity.Id, [], [..watching], Fixtures.Now);
        var row = Fixtures.RowOf(state, _ => _.Build?.PipelineId == "Verify_Build");
        await Assert.That(MonitorSession.OpenMenu(state, row).Menu!.Items.Any(_ => _.Command == CommandKind.RunNext)).IsFalse();
    }

    [Test]
    public async Task TheOverflowLeavesOutAChipTheBuildLost()
    {
        // build-all's Cancel was behind the overflow chip, but the build finished before the click.
        var builds = Fixtures.WithBuilds();
        var finished = Fixtures.JenkinsBuilds().Select(_ => _.PipelineId == "build-all" ? _ with { Status = BuildStatus.Succeeded, CanCancel = false, Finished = Fixtures.Now } : _);
        var state = MonitorSession.ApplyPoll(builds, Fixtures.Jenkins.Id, [], [..finished], Fixtures.Now);
        var row = Fixtures.RowOf(state, _ => _.Build?.PipelineId == "build-all");
        await Assert.That(MonitorSession.OpenOverflow(state, row, ChipKind.Cancel).Menu).IsNull();
    }

    // The device code is drawn as a label, which cannot be selected, so the flow putting it on the
    // clipboard is the only way it gets into the provider's page without being typed out by hand.
    [Test]
    public async Task TheDeviceCodeGoesOnTheClipboard()
    {
        var state = Fixtures.SignInDevice();
        await Assert.That(state.Clipboard).IsEqualTo("ABCD-1234");
    }

    [Test]
    public async Task CopyUserCodePutsTheCodeBackAfterTheClipboardMovedOn()
    {
        var state = Fixtures.SignInDevice();
        var flushed = MonitorSession.Copied(state, state.Clipboard!);
        await Assert.That(flushed.Clipboard).IsNull();
        var again = MonitorSession.CopyUserCode(flushed);
        await Assert.That(again.Clipboard).IsEqualTo("ABCD-1234");
        await Assert.That(again.Status).IsEqualTo("Copied the code");
    }

    // Nothing to copy before the code arrives, so the button is not offered and the transition is
    // a no-op rather than a clipboard set to nothing.
    [Test]
    public async Task CopyUserCodeDoesNothingBeforeTheCodeArrives()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "GitHub Actions");
        state = MonitorSession.FieldChanged(state, FormFields.Auth, nameof(AuthMethod.Device));
        state = MonitorSession.BeginSignIn(state, ConnectionDraft.Build(state.Form!), AuthMethod.Device, Guid.NewGuid());
        await Assert.That(ScreenBuilder.Buttons(state).Select(_ => _.Label)).IsEquivalentTo(["Cancel"]);
        await Assert.That(MonitorSession.CopyUserCode(state).Clipboard).IsNull();
    }

    [Test]
    public async Task CopiedClearsOnlyTheTextThatWasCopied()
    {
        var first = MonitorSession.Copy(Fixtures.WithBuilds(), "first log", "Copied the log");
        var second = MonitorSession.Copy(first, "second log", "Copied the log");
        await Assert.That(MonitorSession.Copied(second, first.Clipboard!).Clipboard).IsEqualTo("second log");
        await Assert.That(MonitorSession.Copied(second, second.Clipboard!).Clipboard).IsNull();
    }

    [Test]
    public async Task ApplyPollReplacesOnlyThatConnection()
    {
        var state = Fixtures.WithBuilds();
        var next = MonitorSession.ApplyPoll(state, Fixtures.Jenkins.Id, [], [], Fixtures.Now);
        await Assert.That(next.Builds.Any(_ => _.ConnectionId == Fixtures.Jenkins.Id)).IsFalse();
        await Assert.That(next.Builds.Count(_ => _.ConnectionId == Fixtures.GitHub.Id)).IsEqualTo(4);
        await Assert.That(next.Connection(Fixtures.Jenkins.Id)!.LastPolled).IsEqualTo(Fixtures.Now);
    }

    [Test]
    public async Task RemoveConnectionDropsItsBuilds()
    {
        var state = MonitorSession.RemoveConnection(Fixtures.WithBuilds(), Fixtures.GitHub.Id);
        await Assert.That(state.Settings.Connections.Length).IsEqualTo(2);
        await Assert.That(state.Connections.Length).IsEqualTo(2);
        await Assert.That(state.Builds.Any(_ => _.ConnectionId == Fixtures.GitHub.Id)).IsFalse();
    }

    [Test]
    public async Task UpsertKeepsHealthOfExisting()
    {
        var state = Fixtures.WithBuilds();
        var renamed = Fixtures.GitHub with { Name = "Hub" };
        var next = MonitorSession.UpsertConnection(state, renamed);
        var connection = next.Connection(Fixtures.GitHub.Id)!;
        await Assert.That(connection.Connection.Name).IsEqualTo("Hub");
        await Assert.That(connection.Health).IsEqualTo(ConnectionHealth.Ok);
        await Assert.That(next.Settings.Connections.Length).IsEqualTo(3);
    }

    [Test]
    public async Task ChangingProviderResetsDependentFields()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Server, "https://custom");
        state = MonitorSession.FieldChanged(state, FormFields.Provider, "GitLab CI");
        var form = state.Form!;
        await Assert.That(form.Value(FormFields.Server)).IsEqualTo("https://gitlab.com");
        await Assert.That(form.Value(FormFields.Auth)).IsEqualTo(nameof(AuthMethod.Browser));
        await Assert.That(form.Values.ContainsKey(FormFields.Scope("group"))).IsTrue();
    }

    [Test]
    public async Task SignInResultForStaleFlowIsIgnored()
    {
        var state = Fixtures.SignInDevice();
        var other = Guid.NewGuid();
        var next = MonitorSession.SignInCompleted(state, other, "someone");
        await Assert.That(next).IsSameReferenceAs(state);
    }

    [Test]
    public async Task SignInCompletedReturnsToEditor()
    {
        var state = Fixtures.SignInDevice();
        var next = MonitorSession.SignInCompleted(state, state.SignIn!.FlowId, "simon");
        await Assert.That(next.Page).IsEqualTo(Page.Connection);
        await Assert.That(next.Form!.SignedIn).IsTrue();
        await Assert.That(next.Form.Message).IsEqualTo("Signed in as simon.");
    }

    [Test]
    public async Task AddAndRemoveFilter()
    {
        var state = MonitorSession.FieldChanged(Fixtures.Filters(), FormFields.FilterText, "Nightly");
        state = MonitorSession.FieldChanged(state, FormFields.FilterKind, nameof(FilterKind.Exact));
        state = MonitorSession.AddFilter(state);
        await Assert.That(state.Form!.Filters.Length).IsEqualTo(2);
        await Assert.That(state.Form.Value(FormFields.FilterText)).IsEqualTo("");

        state = MonitorSession.RemoveFilter(state, 0);
        await Assert.That(state.Form!.Filters.Single().Text).IsEqualTo("Nightly");
    }

    [Test]
    public async Task ExcludePipelineAddsExactFilter()
    {
        var state = Fixtures.WithBuilds();
        var build = state.Builds.First(_ => _.PipelineName == "Nightly");
        var next = MonitorSession.ExcludePipeline(state, build);
        await Assert.That(next.Settings.Filters.Single()).IsEqualTo(new(FilterKind.Exact, FilterTarget.Pipeline, "Nightly"));
        await Assert.That(RowProjection.Rows(next).Any(_ => _.Build?.PipelineName == "Nightly")).IsFalse();
    }

    [Test]
    public async Task ExcludeBranchAddsExactFilter()
    {
        var state = Fixtures.WithBuilds();
        var build = state.Builds.First(_ => _.Branch == "main");
        var next = MonitorSession.ExcludeBranch(state, build);
        await Assert.That(next.Settings.Filters.Single()).IsEqualTo(new(FilterKind.Exact, FilterTarget.Branch, "main"));
        await Assert.That(RowProjection.Rows(next).Any(_ => _.Build?.Branch == "main")).IsFalse();
    }

    [Test]
    public async Task ExcludeRepoAddsExactFilter()
    {
        var state = Fixtures.WithBuilds();
        var build = state.Builds.First();
        var next = MonitorSession.ExcludeRepo(state, build);
        await Assert.That(next.Settings.Filters.Single()).IsEqualTo(new(FilterKind.Exact, FilterTarget.Repo, build.RepoName));
        await Assert.That(RowProjection.Rows(next).Any(_ => _.Build?.RepoName == build.RepoName)).IsFalse();
    }

    [Test]
    public async Task ExcludeOrgAddsExactFilterAndTakesEveryRepoWithIt()
    {
        var state = Fixtures.WithBuilds();
        var build = state.Builds.First(_ => _.RepoName == "VerifyTests/DiffEngine");
        var next = MonitorSession.ExcludeOrg(state, build);
        await Assert.That(next.Settings.Filters.Single()).IsEqualTo(new(FilterKind.Exact, FilterTarget.Org, "VerifyTests"));
        await Assert.That(RowProjection.Rows(next).Any(_ => _.Build?.RepoName.StartsWith("VerifyTests/") == true)).IsFalse();
    }

    [Test]
    public async Task ExcludeOrgDoesNothingForAProviderWithNoOrg()
    {
        var state = Fixtures.WithBuilds();
        var build = state.Builds.First(_ => _.ConnectionId == Fixtures.Octopus.Id);
        await Assert.That(MonitorSession.ExcludeOrg(state, build).Settings.Filters).IsEmpty();
    }
}
