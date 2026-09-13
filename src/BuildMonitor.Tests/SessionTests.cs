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
