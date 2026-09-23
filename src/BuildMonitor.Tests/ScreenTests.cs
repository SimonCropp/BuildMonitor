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

    /// <summary>
    /// The triage started and still collecting: the chip is the hourglass, which the drop down and
    /// this text call Triaging, and the footer says what is being collected. Wider than the fixture
    /// it is built on, so the chip is drawn in the row rather than in the drop down.
    /// </summary>
    [Test]
    public Task Triaging() =>
        Verify(Fixtures.Render(Fixtures.Triaging()));

    /// <summary>
    /// The prompt taken by the window: the chip back as it was, the footer saying what was copied
    /// and where the files went, and the tray popping the one thing someone who left the window to
    /// paste is waiting to hear.
    /// </summary>
    [Test]
    public Task TriagePromptCopied()
    {
        var state = Fixtures.Triaging();
        state = MonitorSession.Triaged(state, state.Triaging.Single(), "the prompt", "Copied a triage prompt for test.yml #77: 3 files in /artifacts/Verify-77-1a2b3c4d");
        return Verify(Fixtures.Render(MonitorSession.Copied(state, state.Clipboard!)));
    }

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
    /// A configured prefix groups the passing builds of two repositories, which the repository name
    /// alone left on a row each.
    /// </summary>
    [Test]
    public Task PrefixGroup() =>
        Verify(Fixtures.Render(Fixtures.WithPrefixGroup()));

    [Test]
    public Task PrefixGroupExpanded() =>
        Verify(Fixtures.Render(MonitorSession.ToggleGroup(Fixtures.WithPrefixGroup(), Fixtures.VerifyPassing)));

    /// <summary>
    /// The right click menu of a row whose project shares a prefix with another: what it could be
    /// grouped by, above what it could be excluded from.
    /// </summary>
    [Test]
    public Task PrefixMenuOpen()
    {
        var state = MonitorSession.ApplySettings(Fixtures.WithPrefixGroup(), Fixtures.Settings());
        return Verify(Fixtures.Render(MonitorSession.OpenMenu(state, Fixtures.RowOf(state, _ => _.Build?.RepoName == "VerifyTests/VerifyXunit"))));
    }

    /// <summary>
    /// Deployments of one Octopus project group, which the server names: they share a group with
    /// nothing typed, and each member names its own project.
    /// </summary>
    [Test]
    public Task ProjectGroupExpanded() =>
        Verify(Fixtures.Render(MonitorSession.ToggleGroup(Fixtures.WithProjectGroup(), Fixtures.StorefrontPassing)));

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
                |   > DiffEngine     github   test.yml @main           [####----] 03:00 left               [Cancel]                    |
                |   ? nightly        jenkins                                      queued 30s               [Cancel]                    |
                |   x Verify         github   test.yml @feature/inline            25m ago       SimonCropp PR 42 [Retry] [Log]         |
                |   x Verify         github   release.yml @main                   50m ago                  [Retry] [Log]               |
                |   + [+] Verify              2 passing                           2h ago                                               |
                | > + DiffEngine     github   docs.yml @main                      23h ago                                              |
                +----------------------------------------------------------------------------------------------------------------------+
                | [Refresh] [Connections] [Options] [Filters] [Hide]                                                     Polled 5s ago |
                +----------------------------------------------------------------------------------------------------------------------+
                tray: Failed "BuildMonitor: Verify failing, 4 running"
                  Open
                  Refresh
                  Connections
                  Options
                  Filters
                  Open logs
                  Raise issue
                  Update
                  Exit
                notify: Error "release.yml failed" "VerifyTests/Verify @main #9"
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
    /// The branch leads with its mark, and is a run of its own for it, whether or not it links
    /// anywhere: a space alone did not say where a pipeline's name ended, and Jenkins, which gives
    /// a branch no page, names its jobs with spaces in them. Nothing else in the cell has one.
    /// </summary>
    [Test]
    public Task TheBranchLeadsWithItsMark() =>
        Verify(ScreenBuilder.Build(Fixtures.WithGreenProject(), Fixtures.Now).Builds!.Rows
            .Select(_ => string.Join(" + ", _.Detail.Select(_ => $"{_.Link}[{_.Icon}]'{_.Text}'"))))
            .Snapshot(
                """
                [
                  Pipeline[]'Build all' + None[]' ' + None[branch]'main',
                  ,
                  Pipeline[]'test.yml' + None[]' ' + Branch[branch]'main',
                  ,
                  Pipeline[]'test.yml' + None[]' ' + Branch[branch]'feature/inline',
                  None[]'2 passing',
                  Build[]'docs.yml' + None[]' ' + Branch[branch]'main'
                ]
                """);

    /// <summary>
    /// A hover is only text, so where the row draws the branch's mark a group's list of its
    /// pipelines writes the text standing in for it. Joined by a space alone, a pipeline and a
    /// branch with spaces in either could be split anywhere.
    /// </summary>
    [Test]
    public async Task AGroupsHoverMarksEachBranch()
    {
        var row = ScreenBuilder.Build(Fixtures.WithGreenProject(), Fixtures.Now).Builds!.Rows.Single(_ => _.Kind == RowKind.Group);
        await Assert.That(row.Tooltip(RowPart.Row)).IsEqualTo("Verify: 2 passing builds\ndocs.yml @main\nnuget.yml @main");
    }

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
                  build-all | provider-jenkins-run | ,
                  Deploy Web | provider-octopus-run | ,
                  DiffEngine | provider-github-run | host-github,
                  nightly | provider-jenkins-run | ,
                  Verify | provider-github-failed | host-github,
                  Verify | host-github | ,
                  DiffEngine | host-github | provider-github-history
                ]
                """);

    /// <summary>
    /// A settled build with no repository at all, as every Octopus deployment is, leads with its
    /// run like a broken one: there is no repository to lead with, and no host mark to draw. The
    /// mark of the service that ran it goes before the name, and the name opens the run; before
    /// this the cell carried no mark and opened nothing, so a column of deployments began in blank
    /// space beside rows that started with a logo.
    /// </summary>
    [Test]
    public Task ARowWithNoRepositoryLeadsWithItsRun() =>
        Verify(ScreenBuilder.Build(MonitorSession.ToggleGroup(Fixtures.WithProjectGroup(), Fixtures.StorefrontPassing), Fixtures.Now).Builds!.Rows
            .Where(_ => _.Provider == "octopus")
            .Select(_ => $"{Link(_.Name, _.NameLink)} | {_.NameIcon} | {_.DetailIcon} | {string.Concat(_.Detail.Select(span => Link(span.Text, span.Link)))}"))
            .Snapshot(
                """
                [
                  [Deploy Web](Build) | provider-octopus-run |  | ,
                  [Deploy Api](Build) | provider-octopus-run |  | Production,
                  [Deploy Database](Build) | provider-octopus-run |  | Production
                ]
                """);

    /// <summary>
    /// A row whose pipeline is named after its project, as an AppVeyor one is, leaves the pipeline
    /// out of its second cell. It still leads with its run while that run is going: the cell it
    /// leads with is chosen by the status, and never by what the cell beside it happens to hold.
    /// </summary>
    [Test]
    public async Task ARowNamedAfterItsProjectStillLeadsWithItsRun()
    {
        var state = Fixtures.WithBuilds();
        var running = Fixtures.Build(
            Fixtures.GitHub.Id,
            "Verify.PdfPig",
            "Verify.PdfPig",
            "VerifyTests/Verify.PdfPig",
            "main",
            "1635",
            BuildStatus.Running,
            started: Fixtures.Now - TimeSpan.FromMinutes(2));
        var builds = state.Builds.Where(_ => _.ConnectionId == Fixtures.GitHub.Id).Append(running);
        var next = MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, [], [..builds], Fixtures.Now);
        var row = ScreenBuilder.Build(next, Fixtures.Now).Builds!.Rows.Single(_ => _.Name == "Verify.PdfPig");

        await Assert.That(row.DetailText).IsEqualTo("main");
        await Assert.That(row.NameLink).IsEqualTo(ChipKind.Build);
        await Assert.That(row.NameIcon).IsEqualTo("provider-github-run");
        await Assert.That(row.DetailIconLink).IsEqualTo(ChipKind.Repo);
        await Assert.That(row.DetailIcon).IsEqualTo("host-github");
    }

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
        // The error first, though GitHub sorts ahead by name: a rate limit needs nothing done.
        await Assert.That(screen.Status).IsEqualTo("Jenkins: error: 500 Internal Server Error (+1 more)");
        await Assert.That(screen.StatusTooltip).IsEqualTo("Jenkins: error: 500 Internal Server Error\nGitHub: rate limited, retrying in 4m");
    }

    static string Link(string text, ChipKind link)
    {
        if (link == ChipKind.None)
        {
            return text;
        }

        return $"[{text}]({link})";
    }

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
        state = MonitorSession.ApplySettings(
            state,
            state.Settings
                with
                {
                    ShowOtherBranches = false
                });
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
    /// A TeamCity connection, whose queue can be reordered: the waiting row carries Run next after
    /// its Cancel, and the running one below it does not.
    /// </summary>
    [Test]
    public Task QueuePriority() =>
        Verify(Fixtures.Render(Fixtures.WithQueuePriority()));

    [Test]
    public Task About() =>
        Verify(Fixtures.Render(Fixtures.About()));

    [Test]
    public Task Connections() =>
        Verify(Fixtures.Render(Fixtures.Connections()));

    [Test]
    public Task ConnectionsEmpty() =>
        Verify(Fixtures.Render(MonitorSession.OpenConnections(Fixtures.Empty())));

    /// <summary>
    /// The connection says why its rows offer no retry or cancel.
    /// </summary>
    [Test]
    public Task ConnectionsWatchOnly() =>
        Verify(Fixtures.Render(MonitorSession.OpenConnections(Fixtures.WatchOnly())));

    [Test]
    public Task Filters() =>
        Verify(Fixtures.Render(Fixtures.Filters()));

    [Test]
    public Task FiltersEmpty() =>
        Verify(Fixtures.Render(MonitorSession.OpenFilters(Fixtures.WithBuilds())));

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

    /// <summary>
    /// What the editor's Remove asks before anything goes. Its Remove sits where Sign in did, so a
    /// second click on the editor's Remove, the fifth button, lands on nothing.
    /// </summary>
    [Test]
    public Task RemoveConnection() =>
        Verify(Fixtures.Render(MonitorSession.OpenRemoveConnection(Fixtures.ConnectionEdit())));

    /// <summary>
    /// The error under the box it is about rather than below the documentation links.
    /// </summary>
    [Test]
    public Task ConnectionValidationError() =>
        Verify(Fixtures.Render(InputApplier.Execute(Fixtures.ConnectionNew(), CommandKind.Save, null, MonitorActions.None, null)));

    /// <summary>
    /// An error the service gave rather than a field, which has nowhere to go but the foot.
    /// </summary>
    [Test]
    public Task ConnectionTestError() =>
        Verify(Fixtures.Render(MonitorSession.SetFormError(Fixtures.ConnectionNew(), new("Unauthorized"))));

    /// <summary>
    /// Under Polling with the interval it is about, rather than under Local below the port.
    /// </summary>
    [Test]
    public Task OptionsError()
    {
        var state = MonitorSession.FieldChanged(Fixtures.Options(), FormFields.PollInterval, "1");
        return Verify(Fixtures.Render(InputApplier.Execute(state, CommandKind.Save, null, MonitorActions.None, null)));
    }

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
