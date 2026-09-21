/// <summary>
/// The connections, on a page of their own rather than under a dozen options, and within one click
/// of the builds page, the tray, and an install with none yet.
/// </summary>
public class ConnectionsPageTests
{
    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());

    static int ButtonIndex(SessionState state, CommandKind command) =>
        ScreenBuilder.Buttons(state).ToList().FindIndex(_ => _.Command == command);

    [Test]
    public async Task TheFooterOpensThem()
    {
        var state = Fixtures.WithBuilds();
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.OpenConnections)), new());
        await Assert.That(state.Page).IsEqualTo(Page.Connections);
    }

    [Test]
    public async Task TheTrayOpensThemInTheWindow()
    {
        var window = new FakeWindow();
        var state = InputApplier.Apply(Fixtures.Hidden(), new(TrayItem: TrayMenu.Connections), new RecordingActions().Actions, window);
        await Assert.That(state.Page).IsEqualTo(Page.Connections);
        await Assert.That(state.Hidden).IsFalse();
    }

    /// <summary>
    /// Back, or Escape, for the builds: each editor saved its own connection, so there is nothing
    /// here for leaving to throw away.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task BackGoesToTheBuilds(bool escape)
    {
        var actions = new RecordingActions();
        var state = Fixtures.Connections();
        MonitorInput back = escape
            ? new(Key: CommandKind.CancelForm)
            : new(ClickedButton: ButtonIndex(state, CommandKind.CancelForm));
        state = Apply(state, back, actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
        await Assert.That(actions.Calls).IsEmpty();
    }

    /// <summary>
    /// Nothing to poll and nothing to list, so neither Refresh nor Connections: the one thing to
    /// do is add one, and it comes first.
    /// </summary>
    [Test]
    public async Task AnInstallWithNoneLeadsWithAddConnection() =>
        await Assert.That(string.Join(", ", ScreenBuilder.Buttons(Fixtures.Empty()).Select(_ => _.Label)))
            .IsEqualTo("Add connection, Options, Filters, Hide");

    /// <summary>
    /// The first connection lands on the builds it is about to fill, rather than on a list of one.
    /// </summary>
    [Test]
    public async Task TheFirstConnectionGoesToTheBuilds()
    {
        var actions = new RecordingActions();
        var state = Fixtures.Empty();
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.AddConnection)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.AddConnection);
        state = Apply(state, new(FieldChanges:
        [
            new(FormFields.Provider, "GitHub Actions"),
            new(FormFields.Auth, nameof(AuthMethod.Token)),
            new(FormFields.Name, "Work"),
            new(FormFields.Token, "ghp_secret")
        ]), actions);
        state = Apply(state, new(ClickedButton: ButtonIndex(state, CommandKind.Save)), actions);
        await Assert.That(state.Page).IsEqualTo(Page.Builds);
        await Assert.That(state.Settings.Connections.Select(_ => _.Name)).IsEquivalentTo(["Work"]);
    }
}
