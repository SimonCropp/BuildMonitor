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

    [Test]
    public Task FailedGroup() =>
        Verify(Fixtures.Render(Fixtures.WithFailedGroup()));

    [Test]
    public Task FailedGroupCollapsed() =>
        Verify(Fixtures.Render(MonitorSession.ToggleGroup(Fixtures.WithFailedGroup(), Fixtures.VerifyFailing)));

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
                |   ? nightly    jenkins                                        queued 30s            [Cancel]                         |
                |   x [-] Verify          2 failing                             25m ago                                                |
                |   x            github   test.yml feature/inline               25m ago    SimonCropp PR 42 [Retry] [Log]              |
                |   x            github   release.yml main                      50m ago               [Retry] [Log]                    |
                |   + [+] Verify          2 passing                             2h ago                                                 |
                | > + DiffEngine github   docs.yml main                         23h ago                                                |
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

    [Test]
    public Task RowsLinkTheRunAndTheBranch() =>
        Verify(ScreenBuilder.Build(Fixtures.WithGreenProject(), Fixtures.Now).Builds!.Rows
            .Select(_ => $"{Link(_.Name, _.NameLink)} | {string.Concat(_.Detail.Select(span => Link(span.Text, span.Link)))}"))
            .Snapshot(
                """
                [
                  build-all | [Build all](Build) main,
                  [Deploy Web](Build) | ,
                  DiffEngine | [test.yml](Build) [main](Branch),
                  [nightly](Build) | ,
                  Verify | [test.yml](Build) [feature/inline](Branch),
                  Verify | 2 passing,
                  DiffEngine | [docs.yml](Build) [main](Branch)
                ]
                """);

    static string Link(string text, ChipKind link) =>
        link == ChipKind.None ? text : $"[{text}]({link})";

    [Test]
    public async Task DependabotBranchesDropTheEcosystem()
    {
        var state = Fixtures.WithDependabotFailure();
        await Assert.That(DetailsOf(MonitorSession.Search(state, "polyfill")))
            .IsEqualTo("Reports | build.yml dependabot/src/Polyfill-9.1.0");
        // The filter box matches the name the row shows, so the ecosystem no longer finds the row.
        await Assert.That(DetailsOf(MonitorSession.Search(state, "nuget"))).IsEqualTo("");
    }

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
