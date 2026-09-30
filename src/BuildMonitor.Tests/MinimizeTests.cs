/// <summary>
/// The head reports whether its window is minimized on every poll, and a minimized window is one
/// nobody can see, so the loop idles over it as it does over a hidden one.
/// </summary>
public class MinimizeTests
{
    static SessionState Apply(SessionState state, MonitorInput input) =>
        InputApplier.Apply(state, input, new RecordingActions().Actions, new FakeWindow());

    [Test]
    public async Task AMinimizedWindowIsUnseen()
    {
        var state = Apply(Fixtures.WithBuilds(), new(Minimized: true));
        await Assert.That(state.Minimized).IsTrue();
        await Assert.That(state.Unseen).IsTrue();
    }

    [Test]
    public async Task ARestoredWindowIsSeenAgain()
    {
        var minimized = Apply(Fixtures.WithBuilds(), new(Minimized: true));
        var restored = Apply(minimized, new(Minimized: false));
        await Assert.That(restored.Unseen).IsFalse();
    }

    /// <summary>
    /// Reported every poll, so nearly every report is the one before it again. A new state would
    /// rebuild the screen each frame, the very work minimizing is meant to spare.
    /// </summary>
    [Test]
    public async Task TheSameReportAgainChangesNothing()
    {
        var minimized = Apply(Fixtures.WithBuilds(), new(Minimized: true));
        var again = Apply(minimized, new(Minimized: true));
        await Assert.That(ReferenceEquals(minimized, again)).IsTrue();
    }

    [Test]
    public async Task AHeadThatDoesNotSayLeavesItAsItWas()
    {
        var minimized = Apply(Fixtures.WithBuilds(), new(Minimized: true));
        await Assert.That(Apply(minimized, new()).Minimized).IsTrue();
    }

    [Test]
    public async Task MinimizingLeavesTheStatusLine()
    {
        var state = MonitorSession.SetStatus(Fixtures.WithBuilds(), "Copied");
        await Assert.That(Apply(state, new(Minimized: true)).Status).IsEqualTo("Copied");
    }
}
