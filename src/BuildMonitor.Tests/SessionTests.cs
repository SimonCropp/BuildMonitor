public class SessionTests
{
    [Test]
    public async Task RowsAreHeadersThenBuilds()
    {
        var rows = RowProjection.Rows(Fixtures.WithBuilds());
        await Verify(rows.Select(_ => $"{_.Kind} {_.Connection.Connection.Name} {_.Build?.PipelineName} {_.Build?.Branch} {_.Build?.Status}"))
            .Snapshot(
                """
                [
                  Header GitHub   ,
                  Build GitHub test.yml main Running,
                  Build GitHub test.yml feature/inline Failed,
                  Build GitHub docs.yml main Succeeded,
                  Header Jenkins   ,
                  Build Jenkins Build all main Running,
                  Build Jenkins Nightly  Queued,
                  Header Octopus   ,
                  Build Octopus Deploy Web  Running
                ]
                """);
    }

    [Test]
    public async Task GreenPipelinesOfOneProjectShareARow()
    {
        var rows = RowProjection.Rows(Fixtures.WithGreenProject());
        var project = rows.Single(_ => _.Kind == RowKind.Project);
        await Assert.That(string.Join(",", project.Members.Select(_ => _.PipelineName))).IsEqualTo("docs.yml,nuget.yml");
        // The failing workflow of the same project keeps its own row.
        await Assert.That(rows.Count(_ => _.Build?.RepoName == "VerifyTests/Verify")).IsEqualTo(1);
        // A project with one green workflow has nothing to share a row with.
        await Assert.That(rows.Count(_ => _.Build is { RepoName: "VerifyTests/DiffEngine", PipelineName: "docs.yml" })).IsEqualTo(1);
    }

    [Test]
    public async Task SingleGreenPipelineDoesNotCollapse() =>
        await Assert.That(RowProjection.Rows(Fixtures.WithBuilds()).Any(_ => _.Kind == RowKind.Project)).IsFalse();

    [Test]
    public async Task ToggleProjectTwiceRestores()
    {
        var state = Fixtures.WithGreenProject();
        var once = MonitorSession.ToggleProject(state, Fixtures.VerifyProject);
        var twice = MonitorSession.ToggleProject(once, Fixtures.VerifyProject);
        await Assert.That(RowProjection.Rows(once).Any(_ => _.Kind == RowKind.Project)).IsFalse();
        await Assert.That(twice.ExpandedProjects).IsEmpty();
        await Assert.That(RowProjection.Rows(twice).Length).IsEqualTo(RowProjection.Rows(state).Length);
    }

    [Test]
    public async Task ToggleProjectKeepsSelectionOnProject()
    {
        var state = MonitorSession.SelectRow(Fixtures.WithGreenProject(), 3);
        await Assert.That(MonitorSession.SelectedRow(state)!.Kind).IsEqualTo(RowKind.Project);

        var expanded = MonitorSession.ToggleProject(state, Fixtures.VerifyProject);
        var selected = MonitorSession.SelectedBuild(expanded)!;
        await Assert.That(selected.ProjectKey).IsEqualTo(Fixtures.VerifyProject);
        await Assert.That(selected.PipelineName).IsEqualTo("docs.yml");

        var collapsed = MonitorSession.ToggleProject(MonitorSession.SelectRow(expanded, 4), Fixtures.VerifyProject);
        await Assert.That(MonitorSession.SelectedRow(collapsed)!.Kind).IsEqualTo(RowKind.Project);
    }

    [Test]
    public async Task ProjectLeavesCollapseWhenAMemberFails()
    {
        var state = Fixtures.WithGreenProject();
        var builds = state.Builds
            .Where(_ => _.ConnectionId == Fixtures.GitHub.Id)
            .Select(_ => _.PipelineId == "Verify/nuget.yml" ? _ with { Status = BuildStatus.Failed } : _)
            .ToImmutableArray();
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], builds, Fixtures.Now);
        var rows = RowProjection.Rows(next);
        await Assert.That(rows.Any(_ => _.Kind == RowKind.Project)).IsFalse();
        await Assert.That(rows.Count(_ => _.Build?.RepoName == "VerifyTests/Verify")).IsEqualTo(3);
    }

    [Test]
    public async Task OpenBuildOnProjectRowExpands()
    {
        var state = MonitorSession.SelectRow(Fixtures.WithGreenProject(), 3);
        var next = InputApplier.Execute(state, CommandKind.OpenBuild, null, MonitorActions.None, null);
        await Assert.That(next.ExpandedProjects).Contains(Fixtures.VerifyProject);
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
        var state = MonitorSession.Resize(Fixtures.WithBuilds(), 120, 12);
        var body = MonitorSession.BodyRows(state);

        var last = MonitorSession.SelectRow(state, 8);
        await Assert.That(last.SelectedRow).IsEqualTo(8);
        await Assert.That(last.ScrollTop).IsEqualTo(8 - body + 1);

        var first = MonitorSession.SelectRow(last, 0);
        await Assert.That(first.ScrollTop).IsEqualTo(0);
    }

    [Test]
    public async Task SelectionSurvivesFewerRows()
    {
        var state = MonitorSession.SelectRow(Fixtures.WithBuilds(), 8);
        var folded = MonitorSession.ToggleGroup(state, Fixtures.GitHub.Id);
        var total = RowProjection.Rows(folded).Length;
        await Assert.That(folded.SelectedRow).IsEqualTo(total - 1);
    }

    [Test]
    public async Task SelectionFollowsItsPipelineToAnotherBranch()
    {
        // Row 3 is docs.yml on main. A run starting on a branch takes over the pipeline's row and,
        // being running, sorts to the top of the group.
        var state = MonitorSession.SelectRow(Fixtures.WithBuilds(), 3);
        var run = Fixtures.Build(Fixtures.GitHub.Id, "DiffEngine/docs.yml", "docs.yml", "VerifyTests/DiffEngine", "feature/x", "301", BuildStatus.Running, started: Fixtures.Now);
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [..Fixtures.GitHubBuilds(), run], Fixtures.Now);
        await Assert.That(MonitorSession.SelectedBuild(next)?.Key).IsEqualTo("gh/DiffEngine/docs.yml/feature/x");
    }

    [Test]
    public async Task SelectionMovesUpWhenItsPipelineIsExcluded()
    {
        var state = MonitorSession.SelectRow(Fixtures.WithBuilds(), 3);
        var next = MonitorSession.ExcludePipeline(state, MonitorSession.SelectedBuild(state)!);
        await Assert.That(MonitorSession.SelectedBuild(next)?.Key).IsEqualTo("gh/Verify/test.yml/feature/inline");
    }

    [Test]
    public async Task FoldingMovesTheSelectionToTheHeader()
    {
        var state = MonitorSession.SelectRow(Fixtures.WithBuilds(), 2);
        var folded = MonitorSession.ToggleGroup(state, Fixtures.GitHub.Id);
        await Assert.That(folded.SelectedRow).IsEqualTo(0);
    }

    [Test]
    public async Task MenuClosesWhenAPollMovesItsRow()
    {
        // Row 1 is the running DiffEngine build, which offers Cancel. A newer run sorts above it,
        // so a menu left where it was drawn would sit on another build.
        var state = MonitorSession.OpenMenu(Fixtures.WithBuilds(), 1);
        var rerun = Fixtures.Build(Fixtures.GitHub.Id, "Verify/test.yml", "test.yml", "VerifyTests/Verify", "feature/inline", "78", BuildStatus.Running, started: Fixtures.Now);
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [..Fixtures.GitHubBuilds(), rerun], Fixtures.Now);
        await Assert.That(next.Menu).IsNull();
        await Assert.That(MonitorSession.SelectedBuild(next)?.Key).IsEqualTo("gh/DiffEngine/test.yml/main");
    }

    [Test]
    public async Task MenuStaysWhenAPollLeavesItsRow()
    {
        var state = MonitorSession.OpenMenu(Fixtures.WithBuilds(), 1);
        var next = MonitorSession.ApplyPoll(state, Fixtures.Jenkins.Id, [], [], Fixtures.Now);
        await Assert.That(next.Menu).IsEqualTo(state.Menu);
    }

    [Test]
    public async Task ToggleGroupTwiceRestores()
    {
        var state = Fixtures.WithBuilds();
        var once = MonitorSession.ToggleGroup(state, Fixtures.GitHub.Id);
        var twice = MonitorSession.ToggleGroup(once, Fixtures.GitHub.Id);
        await Assert.That(once.FoldedGroups).Contains(Fixtures.GitHub.Id);
        await Assert.That(twice.FoldedGroups).IsEmpty();
    }

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
}
