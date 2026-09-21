/// <summary>
/// The footer's way to act on the connection it reports. The footer is the one place always on
/// screen that says a connection is failing, and reaching that connection's editor otherwise took
/// the options page and a click on it there.
/// </summary>
public class FooterConnectionButtonTests
{
    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());

    static int ButtonIndex(SessionState state, CommandKind command) =>
        ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == command);

    [Test]
    [Arguments(ConnectionHealth.NeedsAuth, "Sign in", "Open GitHub to sign in again")]
    [Arguments(ConnectionHealth.Error, "Check connection", "Open GitHub to check its server and credential")]
    public async Task AConnectionThatNeedsTheUserGetsAButton(ConnectionHealth health, string label, string tooltip)
    {
        var state = MonitorSession.SetHealth(Fixtures.WithBuilds(), Fixtures.GitHub.Id, health, "401");
        var button = ScreenBuilder.Buttons(state).Single(_ => _.Command == CommandKind.EditUnhealthyConnection);
        await Assert.That(button.Label).IsEqualTo(label);
        await Assert.That(button.Tooltip).IsEqualTo(tooltip);
    }

    /// <summary>
    /// A rate limit is waited out by the poller, and a poll in flight is about to finish, so
    /// neither offers anything to do.
    /// </summary>
    [Test]
    [Arguments(ConnectionHealth.Ok)]
    [Arguments(ConnectionHealth.Polling)]
    [Arguments(ConnectionHealth.RateLimited)]
    public async Task NothingToDoGetsNoButton(ConnectionHealth health)
    {
        var state = MonitorSession.SetHealth(Fixtures.WithBuilds(), Fixtures.GitHub.Id, health);
        await Assert.That(ButtonIndex(state, CommandKind.EditUnhealthyConnection)).IsEqualTo(-1);
    }

    /// <summary>
    /// Opened from the builds page, so closing it goes back there.
    /// </summary>
    [Test]
    public async Task ItOpensThatConnectionsEditor()
    {
        var actions = new RecordingActions();
        var state = Fixtures.NeedsAuth();
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.EditUnhealthyConnection)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.EditConnection);
        await Assert.That(Fixtures.ConnectionForm(state).ConnectionId).IsEqualTo(Fixtures.GitHub.Id);
        state = Apply(state, new(Key: CommandKind.CancelForm), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
    }

    /// <summary>
    /// GitHub is rate limited and sorts first by name, but Jenkins has the error, so Jenkins is
    /// what the footer names and what its button opens.
    /// </summary>
    [Test]
    public async Task ItOpensTheOneTheFooterNames()
    {
        var actions = new RecordingActions();
        var state = Fixtures.ConnectionErrors();
        await Assert.That(ScreenBuilder.Status(state, Fixtures.Now)).StartsWith("Jenkins:");
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.EditUnhealthyConnection)), actions);
        await Assert.That(Fixtures.ConnectionForm(state).ConnectionId).IsEqualTo(Fixtures.Jenkins.Id);
    }

    /// <summary>
    /// The Undo an exclude leaves stays last, after this button too: it is the one that leaves
    /// with the next input, and one leaving from the middle would move the rest.
    /// </summary>
    [Test]
    public async Task UndoStaysLast()
    {
        var actions = new RecordingActions();
        var builds = Fixtures.NeedsAuth();
        var row = Fixtures.RowOf(builds, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");
        var state = Apply(builds, new(RightClickedRow: row), actions);
        state = Apply(state, new(ClickedMenuItem: state.Menu!.Items.ToList().FindIndex(_ => _.Command == CommandKind.ExcludeBranch)), actions);
        var commands = ScreenBuilder.Buttons(state).Select(_ => _.Command).ToList();
        await Assert.That(commands[^2]).IsEqualTo(CommandKind.EditUnhealthyConnection);
        await Assert.That(commands[^1]).IsEqualTo(CommandKind.UndoExclude);
    }
}
