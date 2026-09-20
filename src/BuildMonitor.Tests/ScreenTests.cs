/// <summary>
/// One snapshot per canonical state, rendered as text. These are the tests every head is held
/// to: a head draws exactly this structure, so a change here is a change on every platform.
/// </summary>
public class ScreenTests
{
    [Test]
    public Task Empty() =>
        Verify(Fixtures.Render(Fixtures.Empty()));

    [Test]
    public Task Connected() =>
        Verify(Fixtures.Render(Fixtures.Connected()));

    [Test]
    public Task Builds() =>
        Verify(Fixtures.Render(Fixtures.WithBuilds()));

    [Test]
    public Task Polling() =>
        Verify(Fixtures.Render(Fixtures.Polling()));

    /// <summary>
    /// The rows of the two repositories found under the code directory carry the open folder chip;
    /// the rest are as they were. Drawn as a picture by every pixel head, so this is the one place
    /// the label it keeps for the drop down is visible.
    /// </summary>
    [Test]
    public Task LocalRepos() =>
        Verify(Fixtures.Render(Fixtures.WithLocalRepos()));

    [Test]
    public Task LocalReposNarrow() =>
        Verify(Fixtures.Render(MonitorSession.Resize(Fixtures.WithLocalRepos(), 80, 30)));

    /// <summary>
    /// The failing row also has a checkout, which is the only shape that carries the triage chip.
    /// Its own fixture rather than a change to the folder one, so every other chip snapshot and
    /// both native baselines stay where they are.
    /// <para>
    /// That row ends up carrying five chips. The chips column reserves room for all five, but only
    /// where the window is wide enough to grant it: at the hundred and twenty columns this renders in, the
    /// last of them are drawn as the drop down. Triage is last in the chip order on purpose: it is
    /// the most expensive thing on the row, so it is the right one to lose first.
    /// <see cref="SessionTests"/> covers the drop down itself offering it.
    /// </para>
    /// </summary>
    [Test]
    public Task Triage() =>
        Verify(Fixtures.Render(Fixtures.WithTriageableFailure()));

    [Test]
    public async Task ProgressIsDroppedWhenThePollEnds()
    {
        var state = Fixtures.Polling();
        await Assert.That(state.Connection(Fixtures.GitHub.Id)!.Progress).IsEqualTo(new PollProgress(15, 20));
        var done = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [], Fixtures.Now);
        await Assert.That(done.Connection(Fixtures.GitHub.Id)!.Progress).IsNull();
        // A late report after the poll finished must not resurrect it.
        var late = MonitorSession.SetProgress(done, Fixtures.GitHub.Id, new(20, 20));
        await Assert.That(late.Connection(Fixtures.GitHub.Id)!.Progress).IsNull();
    }

    [Test]
    public Task GreenGroup() =>
        Verify(Fixtures.Render(Fixtures.WithGreenProject()));

    [Test]
    public Task GreenGroupExpanded() =>
        Verify(Fixtures.Render(MonitorSession.ToggleGroup(Fixtures.WithGreenProject(), Fixtures.VerifyPassing)));

    [Test]
    public Task GreenGroupMenuOpen()
    {
        var state = Fixtures.WithGreenProject();
        return Verify(Fixtures.Render(MonitorSession.OpenMenu(state, Fixtures.RowOf(state, _ => _.Kind == RowKind.Group))));
    }

    [Test]
    public Task GroupMemberMenuOpen()
    {
        var state = MonitorSession.ToggleGroup(Fixtures.WithGreenProject(), Fixtures.VerifyPassing);
        return Verify(Fixtures.Render(MonitorSession.OpenMenu(state, Fixtures.RowOf(state, _ => _.Kind == RowKind.Member))));
    }

    /// <summary>
    /// Two failing workflows of one project, each on a row of its own rather than folded into a
    /// group that named neither.
    /// </summary>
    [Test]
    public Task TwoFailures() =>
        Verify(Fixtures.Render(Fixtures.WithTwoFailures()));

    [Test]
    public Task SingleProvider() =>
        Verify(Fixtures.Render(Fixtures.SingleProvider()));

    [Test]
    public Task ConnectionErrorsInFooter() =>
        Verify(Fixtures.Render(Fixtures.ConnectionErrors()));

    [Test]
    public Task Scrolled() =>
        Verify(Fixtures.Render(Fixtures.Scrolled()))
            .Snapshot(
                """
                +----------------------------------------------------------------------------------------------------------------------+
                | BuildMonitor                                           9 pipelines, 2 failing, 4 running  Filter: [                ] |
                +----------------------------------------------------------------------------------------------------------------------+
                |   > DiffEngine github   test.yml main           [####----] 03:00 left               [Cancel]                         |
                |   ? nightly    jenkins                                     queued 30s               [Cancel]                         |
                |   x Verify     github   test.yml feature/inline            25m ago       SimonCropp PR 42 [Retry] [Log]              |
                |   x Verify     github   release.yml main                   50m ago                  [Retry] [Log]                    |
                |   + [+] Verify          2 passing                          2h ago                                                    |
                | > + DiffEngine github   docs.yml main                      23h ago                                                   |
                +----------------------------------------------------------------------------------------------------------------------+
                | [Refresh] [Options] [Filters] [Hide]                                                                   Polled 5s ago |
                +----------------------------------------------------------------------------------------------------------------------+
                tray: Failed "BuildMonitor: 2 failing, 4 running"
                  Open
                  Refresh
                  Options
                  Filters
                  Open logs
                  Raise issue
                  Update
                  Exit
                notify: "release.yml failed" "VerifyTests/Verify main #9"
                """);

    [Test]
    public Task Narrow() =>
        Verify(Fixtures.Render(Fixtures.Narrow()));

    [Test]
    public Task MenuOpen() =>
        Verify(Fixtures.Render(Fixtures.WithMenu()));

    [Test]
    public Task Searched() =>
        Verify(Fixtures.Render(MonitorSession.Search(Fixtures.WithGreenProject(), "docs")));

    [Test]
    public Task SearchedToNothing() =>
        Verify(Fixtures.Render(MonitorSession.Search(Fixtures.WithBuilds(), "nothing like it")));

    /// <summary>
    /// One destination per part of a row. The square is in because it is the only part of a row
    /// whose pipeline is named after its project, as an AppVeyor or Octopus one is, that reaches
    /// the run at all; the name is the repository's and opens the repository, or is plain text on
    /// the providers that do not report one.
    /// </summary>
    [Test]
    public Task RowsLinkTheRunTheRepositoryAndTheBranch() =>
        Verify(ScreenBuilder.Build(Fixtures.WithGreenProject(), Fixtures.Now).Builds!.Rows
            .Select(_ => $"[{_.StatusLink}] {Link(_.Name, _.NameLink)} | {string.Concat(_.Detail.Select(span => Link(span.Text, span.Link)))}"))
            .Snapshot(
                """
                [
                  [Build] [build-all](Build) | [Build all](Pipeline) main,
                  [Build] [Deploy Web](Build) | ,
                  [Build] [DiffEngine](Build) | [test.yml](Pipeline) [main](Branch),
                  [Build] [nightly](Build) | ,
                  [Build] [Verify](Build) | [test.yml](Pipeline) [feature/inline](Branch),
                  [None] [Verify](Repo) | 2 passing,
                  [Build] [DiffEngine](Repo) | [docs.yml](Build) [main](Branch)
                ]
                """);

    /// <summary>
    /// The two marks, and which way round they sit: a row that broke or is still running leads
    /// with the service that ran it, and carries the host of its source in the second cell; a
    /// settled one is the other way round. The two are never the same picture, and a host nothing
    /// here has a mark for leaves its cell with none.
    /// </summary>
    [Test]
    public Task RowsLeadWithTheMarkOfWhatTheyAreAbout() =>
        Verify(ScreenBuilder.Build(Fixtures.WithGreenProject(), Fixtures.Now).Builds!.Rows
            .Select(_ => $"{_.Name} | {_.NameIcon} | {_.DetailIcon}"))
            .Snapshot(
                """
                [
                  build-all | provider-jenkins | ,
                  Deploy Web | provider-octopus | ,
                  DiffEngine | provider-github | host-github,
                  nightly | provider-jenkins | ,
                  Verify | provider-github-failed | host-github,
                  Verify | host-github | ,
                  DiffEngine | host-github | provider-github
                ]
                """);

    /// <summary>
    /// A group's members get their pipeline back, because their first cell is blank and the
    /// pipeline is the only text left that can reach the run. The group's own row links the
    /// repository its members share, which is otherwise named nowhere a click could reach.
    /// </summary>
    [Test]
    public Task AGroupNamesItsMembersPipelines() =>
        Verify(ScreenBuilder.Build(MonitorSession.ToggleGroup(Fixtures.WithGreenProject(), Fixtures.VerifyPassing), Fixtures.Now).Builds!.Rows
            .Where(_ => _.Kind is RowKind.Group or RowKind.Member)
            .Select(_ => $"{_.Kind}: {Link(_.Name, _.NameLink)} | {string.Concat(_.Detail.Select(span => Link(span.Text, span.Link)))}"))
            .Snapshot(
                """
                [
                  Group: [Verify](Repo) | 2 passing,
                  Member:  | [docs.yml](Build) [main](Branch),
                  Member:  | [nuget.yml](Build) [main](Branch)
                ]
                """);

    /// <summary>
    /// The footer names only the first failing connection and cuts its error off at the window's
    /// edge, so the hover is the one place the rest of it can be read.
    /// </summary>
    [Test]
    public async Task TheFooterSaysEveryFailingConnectionOnHover()
    {
        var screen = ScreenBuilder.Build(Fixtures.ConnectionErrors(), Fixtures.Now);
        await Assert.That(screen.Status).IsEqualTo("GitHub: rate limited, retrying in 4m (+1 more)");
        await Assert.That(screen.StatusTooltip).IsEqualTo("GitHub: rate limited, retrying in 4m\nJenkins: error: 500 Internal Server Error");
    }

    static string Link(string text, ChipKind link) =>
        link == ChipKind.None ? text : $"[{text}]({link})";

    [Test]
    public async Task DependabotBranchesAreARobotAndThePackage()
    {
        var state = Fixtures.WithDependabotFailure();
        await Assert.That(DetailsOf(MonitorSession.Search(state, "polyfill")))
            .IsEqualTo("Reports | build.yml 🤖 Polyfill-9.1.0");
        // The filter box matches the name the row shows, so neither the ecosystem nor the prefix
        // the robot stands for finds the row any more.
        await Assert.That(DetailsOf(MonitorSession.Search(state, "nuget"))).IsEqualTo("");
        await Assert.That(DetailsOf(MonitorSession.Search(state, "dependabot"))).IsEqualTo("");
    }

    /// <summary>
    /// What the row is left saying without the words, robot and all.
    /// </summary>
    [Test]
    public Task ADependabotBranchIsARobotAndThePackage() =>
        Verify(Fixtures.Render(Fixtures.WithDependabotFailure()));

    static string DetailsOf(SessionState state) =>
        string.Join(", ", ScreenBuilder.Build(state, Fixtures.Now).Builds!.Rows
            .Select(_ => $"{_.Name} | {string.Concat(_.Detail.Select(span => span.Text))}"));

    [Test]
    public Task OverflowMenuOpen()
    {
        // Ninety columns leave the failed row room for its pull request but not for its actions.
        var state = MonitorSession.Resize(Fixtures.WithBuilds(), 90, 30);
        var row = Fixtures.RowOf(state, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");
        return Verify(Fixtures.Render(MonitorSession.OpenOverflow(state, row, ChipKind.Retry)));
    }

    [Test]
    public Task DefaultBranchOnly()
    {
        var state = Fixtures.WithBuilds();
        state = MonitorSession.ApplySettings(state, state.Settings with { ShowOtherBranches = false });
        return Verify(Fixtures.Render(state));
    }

    [Test]
    public Task Options() =>
        Verify(Fixtures.Render(Fixtures.Options()));

    /// <summary>
    /// What the update warns about before the tray goes away: the servers it is about to stop, and
    /// whose they are.
    /// </summary>
    [Test]
    public Task Update() =>
        Verify(Fixtures.Render(Fixtures.Update()));

    /// <summary>
    /// The same servers where a running file can be replaced, which is everywhere but Windows.
    /// Nothing is stopped, so the page reports rather than warns.
    /// </summary>
    [Test]
    public Task UpdateWithoutStoppingServers() =>
        Verify(Fixtures.Render(Fixtures.Update(stopped: false)));

    /// <summary>
    /// Nothing else running, which is the ordinary case and the one that still has to say what the
    /// update is about to do.
    /// </summary>
    [Test]
    public Task UpdateWithNoServers() =>
        Verify(Fixtures.Render(MonitorSession.OpenUpdate(Fixtures.WithBuilds(), McpServers.None)));

    /// <summary>
    /// The GitHub rows lose Retry and Cancel; Jenkins and Octopus keep theirs.
    /// </summary>
    [Test]
    public Task WatchOnly() =>
        Verify(Fixtures.Render(Fixtures.WatchOnly()));

    /// <summary>
    /// The connection says why its rows offer no retry or cancel.
    /// </summary>
    [Test]
    public Task OptionsWatchOnly() =>
        Verify(Fixtures.Render(MonitorSession.OpenOptions(Fixtures.WatchOnly())));

    [Test]
    public Task Filters() =>
        Verify(Fixtures.Render(Fixtures.Filters()));

    [Test]
    public Task FiltersError() =>
        Verify(Fixtures.Render(MonitorSession.AddFilter(Fixtures.Filters())));

    [Test]
    public Task ConnectionNew() =>
        Verify(Fixtures.Render(Fixtures.ConnectionNew()));

    [Test]
    public Task ConnectionNewGitHubDevice()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "GitHub Actions");
        state = MonitorSession.FieldChanged(state, FormFields.Auth, nameof(AuthMethod.Device));
        return Verify(Fixtures.Render(state));
    }

    // The note about work or school accounts, which a user with a personal Microsoft account needs
    // before starting a sign in Entra will refuse rather than after.
    [Test]
    public Task ConnectionNewAzureDevOpsBrowser()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "Azure DevOps");
        state = MonitorSession.FieldChanged(state, FormFields.Auth, nameof(AuthMethod.Browser));
        return Verify(Fixtures.Render(state));
    }

    // The same provider with a token, where the note does not apply and would only be noise.
    [Test]
    public Task ConnectionNewAzureDevOpsToken()
    {
        var state = MonitorSession.FieldChanged(Fixtures.ConnectionNew(), FormFields.Provider, "Azure DevOps");
        state = MonitorSession.FieldChanged(state, FormFields.Auth, nameof(AuthMethod.Token));
        return Verify(Fixtures.Render(state));
    }

    [Test]
    public Task ConnectionEdit() =>
        Verify(Fixtures.Render(Fixtures.ConnectionEdit()));

    [Test]
    public Task ConnectionValidationError() =>
        Verify(Fixtures.Render(InputApplier.Execute(Fixtures.ConnectionNew(), CommandKind.Save, null, MonitorActions.None, null)));

    [Test]
    public Task SignInDevice() =>
        Verify(Fixtures.Render(Fixtures.SignInDevice()));

    [Test]
    public Task NeedsAuth() =>
        Verify(Fixtures.Render(Fixtures.NeedsAuth()));

    [Test]
    public Task RateLimited() =>
        Verify(Fixtures.Render(Fixtures.RateLimited()));

    [Test]
    public Task Hidden() =>
        Verify(Fixtures.Render(Fixtures.Hidden()));

    [Test]
    public Task Status() =>
        Verify(Fixtures.Render(MonitorSession.SetStatus(Fixtures.WithBuilds(), "Retrying test.yml #77")));

    [Test]
    public Task OverRun()
    {
        var state = Fixtures.WithBuilds();
        state = MonitorSession.ApplyMedians(
            state,
            ImmutableDictionary<string, TimeSpan>.Empty.Add("gh/DiffEngine/test.yml", TimeSpan.FromMinutes(2)));
        return Verify(Fixtures.Render(state));
    }
}
