/// <summary>
/// A click on a row a poll has only just changed. Running builds sort to the top and poll every ten
/// seconds, so a row can change between looking at its chip and clicking it, and the click would
/// then retry, cancel or reorder another build on someone's CI.
/// </summary>
public class MovedRowTests
{
    static DateTimeOffset polled = Fixtures.Now;
    const string docs = "gh/DiffEngine/docs.yml/main";
    const string verify = "gh/Verify/test.yml/feature/inline";

    static SessionState Apply(SessionState state, MonitorInput input, RecordingActions actions) =>
        InputApplier.Apply(state, input, actions.Actions, new FakeWindow());

    static SessionState Poll(SessionState state, Func<Build, Build> docsRun)
    {
        ImmutableArray<Build> builds = [..Fixtures.GitHubBuilds().Select(_ => _.Key == docs ? docsRun(_) : _)];
        return MonitorSession.ApplyPoll(state, Fixtures.GitHub.Id, state.Connection(Fixtures.GitHub.Id)!.Pipelines, builds, polled);
    }

    /// <summary>
    /// docs.yml's run failed where it stood: the same row, now drawn with Retry and Log where it
    /// had none.
    /// </summary>
    static SessionState DocsFailedInPlace(SessionState state) =>
        Poll(state, _ => _ with { Status = BuildStatus.Failed, Finished = polled });

    /// <summary>
    /// A new docs.yml run, started a minute ago, failed. The newest failure, it takes the place of
    /// the Verify failure, which moves down one.
    /// </summary>
    static SessionState DocsFailedAnew(SessionState state) =>
        Poll(state, _ => _ with { RunNumber = "301", Status = BuildStatus.Failed, Started = polled - TimeSpan.FromMinutes(1), Finished = polled });

    /// <summary>
    /// Where the build's row is drawn: the index a head reports a click on.
    /// </summary>
    static int Drawn(SessionState state, string key) =>
        Fixtures.RowOf(state, _ => _.Build?.Key == key) - state.ScrollTop;

    static MonitorInput Click(int drawn, ChipKind chip, double secondsAfterThePoll) =>
        new(ClickedChipRow: drawn, ClickedChip: chip, At: polled + TimeSpan.FromSeconds(secondsAfterThePoll));

    /// <summary>
    /// Aimed at the Verify failure's Retry, which the poll moved down, the click lands on docs.yml's.
    /// </summary>
    [Test]
    public async Task AClickOnARowThatJustMovedIsHeld()
    {
        var actions = new RecordingActions();
        var before = Fixtures.WithBuilds();
        var state = DocsFailedAnew(before);
        state = Apply(state, Click(Drawn(before, verify), ChipKind.Retry, 0.5), actions);
        await Assert.That(actions.Calls).IsEmpty();
        await Assert.That(state.Status).IsEqualTo("The rows moved as you clicked, so nothing was retried: click again to retry docs.yml #301");
    }

    /// <summary>
    /// The same row in another status is another set of chips under the pointer, as a Cancel
    /// becomes a Retry when a build finishes where it stood.
    /// </summary>
    [Test]
    public async Task ARowWhoseStatusJustChangedIsHeld()
    {
        var actions = new RecordingActions();
        var state = DocsFailedInPlace(Fixtures.WithBuilds());
        state = Apply(state, Click(Drawn(state, docs), ChipKind.Retry, 0.5), actions);
        await Assert.That(actions.Calls).IsEmpty();
        await Assert.That(state.Status).IsEqualTo("The rows moved as you clicked, so nothing was retried: click again to retry docs.yml #300");
    }

    /// <summary>
    /// Once the move has had time to be seen, the click is taken as meant for the row now there.
    /// </summary>
    [Test]
    public async Task AClickOnceTheMoveHasBeenSeenActs()
    {
        var actions = new RecordingActions();
        var before = Fixtures.WithBuilds();
        var state = DocsFailedAnew(before);
        Apply(state, Click(Drawn(before, verify), ChipKind.Retry, 2), actions);
        await Assert.That(actions.Calls).IsEquivalentTo([$"Retry {docs}"]);
    }

    /// <summary>
    /// Only the positions the poll changed wait: a running row it left where it was cancels at once.
    /// </summary>
    [Test]
    public async Task ARowThePollLeftInPlaceActsAtOnce()
    {
        var actions = new RecordingActions();
        var state = DocsFailedAnew(Fixtures.WithBuilds());
        Apply(state, Click(Drawn(state, "jenkins/build-all/main"), ChipKind.Cancel, 0.5), actions);
        await Assert.That(actions.Calls).IsEquivalentTo(["Cancel jenkins/build-all/main"]);
    }

    /// <summary>
    /// A list scrolled since the poll was aimed at afresh, so the positions it moved no longer
    /// stand for anything under the pointer.
    /// </summary>
    [Test]
    public async Task AClickAfterScrollingActs()
    {
        var actions = new RecordingActions();
        // Three rows high and scrolled to the last three, two of which the poll swaps.
        var state = DocsFailedAnew(MonitorSession.ScrollTo(MonitorSession.Resize(Fixtures.WithBuilds(), 120, 9), 3));
        await Assert.That(state.Moved!.Positions.Count).IsEqualTo(2);
        state = Apply(state, new(ScrollDelta: -1), actions);
        Apply(state, Click(Drawn(state, docs), ChipKind.Retry, 0.5), actions);
        await Assert.That(actions.Calls).IsEquivalentTo([$"Retry {docs}"]);
    }

    /// <summary>
    /// A chip the build does not offer is not held back with a promise a second click cannot keep:
    /// it does nothing, as it always did.
    /// </summary>
    [Test]
    public async Task AChipTheBuildDoesNotOfferIsNotHeld()
    {
        var actions = new RecordingActions();
        var state = DocsFailedInPlace(Fixtures.WithBuilds());
        state = Apply(state, Click(Drawn(state, docs), ChipKind.Cancel, 0.5), actions);
        await Assert.That(actions.Calls).IsEmpty();
        await Assert.That(state.Status).IsEqualTo("");
    }
}
