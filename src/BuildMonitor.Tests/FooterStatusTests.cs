/// <summary>
/// What the one footer line says when more than one thing wants it: the message from the last
/// click, a connection that is not working, a poll the user is waiting on, and the age of the
/// last one.
/// </summary>
public class FooterStatusTests
{
    static string Status(SessionState state) =>
        ScreenBuilder.Status(state, Fixtures.Now);

    [Test]
    public async Task ThePollAgeIsTheFallback() =>
        await Assert.That(Status(Fixtures.WithBuilds())).IsEqualTo("Polled 5s ago");

    [Test]
    public async Task APollInFlightCountsDown() =>
        await Assert.That(Status(Fixtures.Polling())).IsEqualTo("GitHub: polling 15/20");

    /// <summary>
    /// The count the footer shortens to has to point at something, and with no problem to list
    /// the tooltip is otherwise empty.
    /// </summary>
    [Test]
    public async Task SeveralPollsCountIntoATooltip()
    {
        var state = MonitorSession.SetHealth(Fixtures.Polling(), Fixtures.Jenkins.Id, ConnectionHealth.Polling);
        var screen = ScreenBuilder.Build(state, Fixtures.Now);
        await Assert.That(screen.Status).IsEqualTo("GitHub: polling 15/20 (+1 more)");
        await Assert.That(screen.StatusTooltip).IsEqualTo("GitHub: polling 15/20\nJenkins: polling");
    }

    [Test]
    public async Task NothingWrongAndNoPollLeavesNoTooltip() =>
        await Assert.That(ScreenBuilder.Build(Fixtures.WithBuilds(), Fixtures.Now).StatusTooltip).IsEqualTo("");

    /// <summary>
    /// A provider that does not count its work still says a poll is out, rather than falling back
    /// to an age that is about to be replaced.
    /// </summary>
    [Test]
    public async Task APollWithNoCountsStillSaysSo()
    {
        var state = MonitorSession.SetHealth(Fixtures.WithBuilds(), Fixtures.GitHub.Id, ConnectionHealth.Polling);
        await Assert.That(Status(state)).IsEqualTo("GitHub: polling");
    }

    /// <summary>
    /// A poll is over in seconds and a dead credential is not, so the one worth the line is the
    /// one that will still be true when the user looks.
    /// </summary>
    [Test]
    public async Task AProblemOutranksAPollInFlight()
    {
        var state = MonitorSession.SetHealth(Fixtures.Polling(), Fixtures.Jenkins.Id, ConnectionHealth.NeedsAuth, "401");
        await Assert.That(Status(state)).IsEqualTo("Sign in required for Jenkins");
    }

    /// <summary>
    /// The message stays until the next click, so a problem it hid would be hidden for as long as
    /// the window sat untouched. The count is what says to hover.
    /// </summary>
    [Test]
    public async Task AMessageCarriesTheProblemsItCoversFor()
    {
        var state = MonitorSession.SetStatus(Fixtures.NeedsAuth(), "Retrying test.yml #77");
        await Assert.That(Status(state)).IsEqualTo("Retrying test.yml #77 (1 problem)");
    }

    [Test]
    public async Task AMessageCountsEveryProblem()
    {
        var state = MonitorSession.SetHealth(Fixtures.NeedsAuth(), Fixtures.Jenkins.Id, ConnectionHealth.Error, "500");
        state = MonitorSession.SetStatus(state, "Options saved");
        await Assert.That(Status(state)).IsEqualTo("Options saved (2 problems)");
    }

    [Test]
    public async Task AMessageWithNothingWrongIsLeftAlone()
    {
        var state = MonitorSession.SetStatus(Fixtures.WithBuilds(), "Options saved");
        await Assert.That(Status(state)).IsEqualTo("Options saved");
    }

    /// <summary>
    /// Refresh sets no message of its own: it used to set "Refreshing", which outranked the
    /// counting poll it had just asked for and stayed there until the next click.
    /// </summary>
    [Test]
    public async Task RefreshLeavesTheLineToThePoll()
    {
        var actions = new RecordingActions();
        var state = InputApplier.Apply(Fixtures.WithBuilds(), new(Key: CommandKind.Refresh), actions.Actions, new FakeWindow());
        await Assert.That(state.Status).IsEqualTo("");
        await Assert.That(actions.Calls).IsEquivalentTo(["Refresh all"]);
    }
}
