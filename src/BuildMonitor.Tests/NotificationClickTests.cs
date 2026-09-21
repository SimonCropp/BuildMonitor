/// <summary>
/// What a click on the failure notification does. It used to do nothing at all, at the one
/// moment the user is already looking for that build.
/// </summary>
public class NotificationClickTests
{
    static SessionState Click(SessionState state, string? key) =>
        InputApplier.Apply(state, new(ClickedNotification: key), new RecordingActions().Actions, new FakeWindow());

    /// <summary>
    /// The key the notification carried, which is the build's own, so the row it lands on is the
    /// one that failed rather than whichever row that index now holds.
    /// </summary>
    [Test]
    public async Task ItLandsOnTheBuildThatFailed()
    {
        var state = MonitorSession.Hide(Fixtures.WithBuilds());
        var failed = RowProjection.Builds(state).Single(_ => _.Status == BuildStatus.Failed);
        var clicked = Click(state, failed.Key);
        await Assert.That(clicked.Hidden).IsFalse();
        await Assert.That(MonitorSession.SelectedBuild(clicked)!.Key).IsEqualTo(failed.Key);
    }

    [Test]
    public async Task ItShowsTheWindow()
    {
        var window = new FakeWindow();
        var state = MonitorSession.Hide(Fixtures.WithBuilds());
        var failed = RowProjection.Builds(state).Single(_ => _.Status == BuildStatus.Failed);
        InputApplier.Apply(state, new(ClickedNotification: failed.Key), new RecordingActions().Actions, window);
        await Assert.That(window.Calls).IsEquivalentTo(["SetHidden False", "Focus"]);
    }

    /// <summary>
    /// A form open when the failure popped would otherwise take the click and leave the user on
    /// the options page, with the row they were sent to behind it.
    /// </summary>
    [Test]
    public async Task ItClosesWhateverFormWasOpen()
    {
        var state = MonitorSession.OpenOptions(Fixtures.WithBuilds());
        var failed = RowProjection.Builds(state).Single(_ => _.Status == BuildStatus.Failed);
        var clicked = Click(state, failed.Key);
        await Assert.That(clicked.Page).IsEqualTo(Page.Builds);
        await Assert.That(MonitorSession.SelectedBuild(clicked)!.Key).IsEqualTo(failed.Key);
    }

    /// <summary>
    /// Several failures at once name no single row, so the click only brings the window up and
    /// the selection stays where the user left it.
    /// </summary>
    [Test]
    public async Task SeveralFailuresSelectNothing()
    {
        var state = MonitorSession.SelectRow(MonitorSession.Hide(Fixtures.WithBuilds()), 2);
        var clicked = Click(state, "");
        await Assert.That(clicked.Hidden).IsFalse();
        await Assert.That(clicked.SelectedRow).IsEqualTo(2);
    }

    /// <summary>
    /// A build can be retried, filtered or aged out between the notification popping and the
    /// click on it. Moving the selection somewhere arbitrary reads worse than leaving it.
    /// </summary>
    [Test]
    public async Task ABuildThatHasGoneLeavesTheSelection()
    {
        var state = MonitorSession.SelectRow(Fixtures.WithBuilds(), 2);
        var clicked = Click(state, "gh/Gone/none.yml/main");
        await Assert.That(clicked.SelectedRow).IsEqualTo(2);
    }
}
