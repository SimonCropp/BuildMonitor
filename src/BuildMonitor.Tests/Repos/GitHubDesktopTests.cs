/// <summary>
/// The clone chip, its menu item and its click all go through <see cref="GitHubDesktop.CloneLink(SessionState, Build)"/>,
/// so these pin each half of that rule and then that every surface agrees with it.
/// </summary>
public class GitHubDesktopTests
{
    const string verifyLink = "x-github-client://openRepo/https://github.com/VerifyTests/Verify";

    static int FailedRow(SessionState state) =>
        Fixtures.RowOf(state, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");

    static int RunningRow(SessionState state) =>
        Fixtures.RowOf(state, _ => _.Build?.Key == "gh/DiffEngine/test.yml/main");

    static Build BuildAt(SessionState state, int row) =>
        MonitorSession.SelectedBuild(MonitorSession.SelectRow(state, row))!;

    static IEnumerable<ChipKind> ChipsAt(SessionState state, int row) =>
        ScreenBuilder.Build(state, Fixtures.Now).Builds!.Rows[row].Chips.Select(_ => _.Kind);

    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());

    [Test]
    public async Task AGitHubRepositoryWithNoCheckoutIsOfferedAClone()
    {
        var state = Fixtures.WithGitHubDesktop();
        await Assert.That(GitHubDesktop.CloneLink(state, BuildAt(state, FailedRow(state)))).IsEqualTo(verifyLink);
    }

    [Test]
    public async Task NoCloneWithoutGitHubDesktop()
    {
        var state = Fixtures.WithLocalRepos();
        await Assert.That(GitHubDesktop.CloneLink(state, BuildAt(state, FailedRow(state)))).IsNull();
        await Assert.That(ChipsAt(state, FailedRow(state))).DoesNotContain(ChipKind.Clone);
    }

    /// <summary>
    /// The index is empty before the first scan lands and while no code directory is set, the same
    /// as a scan that found nothing. Taken as one, every GitHub row offered a clone, the
    /// repositories already checked out included.
    /// </summary>
    [Test]
    public async Task NoCloneBeforeACodeDirectoryHasBeenScanned()
    {
        var state = MonitorSession.FoundGitHubDesktop(Fixtures.WithBuilds(), true);
        await Assert.That(GitHubDesktop.CloneLink(state, BuildAt(state, FailedRow(state)))).IsNull();
        await Assert.That(ChipsAt(state, FailedRow(state))).DoesNotContain(ChipKind.Clone);
    }

    /// <summary>
    /// A rescan with the code directory cleared, which is what the options page saves.
    /// </summary>
    [Test]
    public async Task NoCloneOnceTheCodeDirectoryIsCleared()
    {
        var state = MonitorSession.ApplyLocalRepos(Fixtures.WithGitHubDesktop(), ImmutableDictionary<string, string>.Empty, null);
        await Assert.That(GitHubDesktop.CloneLink(state, BuildAt(state, FailedRow(state)))).IsNull();
    }

    [Test]
    public async Task ACheckedOutRepositoryOffersItsFolderRatherThanAClone()
    {
        var state = Fixtures.WithGitHubDesktop();
        var row = RunningRow(state);
        await Assert.That(GitHubDesktop.CloneLink(state, BuildAt(state, row))).IsNull();
        await Assert.That(ChipsAt(state, row)).Contains(ChipKind.OpenDirectory);
        await Assert.That(ChipsAt(state, row)).DoesNotContain(ChipKind.Clone);
    }

    [Test]
    [Arguments("https://gitlab.com/VerifyTests/Verify")]
    [Arguments("https://bitbucket.org/VerifyTests/Verify")]
    [Arguments("https://example.com/github/Verify")]
    [Arguments(null)]
    public async Task ARepositoryNotOnGitHubIsNotOffered(string? url)
    {
        var state = Fixtures.WithGitHubDesktop();
        var build = BuildAt(state, FailedRow(state)) with
        {
            RepoUrl = url
        };
        await Assert.That(GitHubDesktop.CloneLink(state, build)).IsNull();
    }

    /// <summary>
    /// An enterprise server carries the GitHub mark, so it is offered too: Desktop clones from one
    /// as readily as from github.com. A trailing slash is dropped, since Desktop adds the .git to
    /// whatever it is handed.
    /// </summary>
    [Test]
    [Arguments("https://github.example.com/team/app", "x-github-client://openRepo/https://github.example.com/team/app")]
    [Arguments("https://github.com/VerifyTests/Verify/", verifyLink)]
    public async Task TheLinkCarriesTheRepositoryPage(string url, string expected)
    {
        var state = Fixtures.WithGitHubDesktop();
        var build = BuildAt(state, FailedRow(state)) with
        {
            RepoUrl = url
        };
        await Assert.That(GitHubDesktop.CloneLink(state, build)).IsEqualTo(expected);
    }

    [Test]
    public async Task ClickingTheCloneChipHandsTheRepositoryToGitHubDesktop()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithGitHubDesktop();
        var row = FailedRow(builds);
        await Assert.That(ChipsAt(builds, row)).Contains(ChipKind.Clone);

        var state = Apply(builds, new(ClickedChipRow: row, ClickedChip: ChipKind.Clone), actions);
        await Assert.That(actions.Calls).IsEquivalentTo([$"OpenUrl {verifyLink}"]);
        await Assert.That(state.Status).IsEqualTo("Opened GitHub Desktop to clone VerifyTests/Verify");
    }

    /// <summary>
    /// A stale frame can still report a clone on a row whose checkout has been found since, and
    /// the applier asks the rule again rather than trusting the chip.
    /// </summary>
    [Test]
    public async Task AStaleCloneClickOnACheckedOutRowDoesNothing()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithGitHubDesktop();
        var state = Apply(builds, new(ClickedChipRow: RunningRow(builds), ClickedChip: ChipKind.Clone), actions);
        await Assert.That(actions.Calls).IsEmpty();
        await Assert.That(state.Status).IsEqualTo(builds.Status);
    }

    [Test]
    public async Task TheMenuOffersTheCloneTheRowDoes()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.WithGitHubDesktop();
        var menu = Apply(builds, new(RightClickedRow: FailedRow(builds)), actions);
        var index = menu.Menu!.Items.ToList().FindIndex(_ => _.Command == CommandKind.CloneRepo);
        await Assert.That(menu.Menu.Items[index].Label).IsEqualTo("Clone in GitHub Desktop");

        Apply(menu, new(ClickedMenuItem: index), actions);
        await Assert.That(actions.Calls).IsEquivalentTo([$"OpenUrl {verifyLink}"]);
    }

    [Test]
    public async Task TheMenuOfACheckedOutRowHasNoClone()
    {
        var builds = Fixtures.WithGitHubDesktop();
        var menu = Apply(builds, new(RightClickedRow: RunningRow(builds)), new());
        await Assert.That(menu.Menu!.Items.Select(_ => _.Command)).DoesNotContain(CommandKind.CloneRepo);
    }

    /// <summary>
    /// The drop down holds the chips a narrow row has no room for, from the same list, so the clone
    /// is in it too.
    /// </summary>
    [Test]
    public async Task TheDropDownOffersTheClone()
    {
        var builds = Fixtures.WithGitHubDesktop();
        var menu = MonitorSession.OpenOverflow(builds, FailedRow(builds), ChipKind.Clone).Menu!;
        await Assert.That(menu.Items.Select(_ => _.Label)).IsEquivalentTo(["Clone"]);
        await Assert.That(menu.Items.Select(_ => _.Command)).IsEquivalentTo([CommandKind.CloneRepo]);
    }

    [Test]
    public async Task FindingItAgainIsTheSameState()
    {
        var state = Fixtures.WithGitHubDesktop();
        await Assert.That(MonitorSession.FoundGitHubDesktop(state, true)).IsSameReferenceAs(state);
        await Assert.That(MonitorSession.FoundGitHubDesktop(state, false).GitHubDesktop).IsFalse();
    }
}
