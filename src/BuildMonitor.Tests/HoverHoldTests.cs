/// <summary>
/// The pointer resting on a row's button, which holds the rows still until it is clicked. Running
/// builds sort to the top and poll every ten seconds, so the row a pointer is halfway to can move
/// out from under it, and the click then retries, cancels or reorders another build on someone's
/// CI. MovedRowTests covers what happens to a click that lands after a move anyway.
/// </summary>
public class HoverHoldTests
{
    static SessionState Apply(SessionState state, MonitorInput input) =>
        InputApplier.Apply(state, input, new RecordingActions().Actions, new FakeWindow());

    [Test]
    public async Task APointerOnAChipHoldsTheRowsStill()
    {
        var state = Apply(Fixtures.WithBuilds(), new(HoveredChipRow: 1, HoveredChip: ChipKind.Retry));
        await Assert.That(state.Hover).IsEqualTo(new HoverState(1, ChipKind.Retry));
        await Assert.That(state.HoldsRows).IsTrue();
    }

    /// <summary>
    /// The head reports where the pointer is on every frame, so most of a hover is the same report
    /// again. The same instance comes back, or the screen would be rebuilt, and on Windows every
    /// row repainted, sixty times a second while the pointer rested on a chip.
    /// </summary>
    [Test]
    public async Task TheSameHoverReportedAgainChangesNothing()
    {
        var hovering = Apply(Fixtures.WithBuilds(), new(HoveredChipRow: 1, HoveredChip: ChipKind.Retry));
        var again = Apply(hovering, new(HoveredChipRow: 1, HoveredChip: ChipKind.Retry));
        await Assert.That(ReferenceEquals(hovering, again)).IsTrue();
    }

    [Test]
    public async Task ThePointerMovingOffTheButtonsLetsTheRowsGo()
    {
        var hovering = Apply(Fixtures.WithBuilds(), new(HoveredChipRow: 1, HoveredChip: ChipKind.Retry));
        var off = Apply(hovering, new());
        await Assert.That(off.Hover).IsNull();
        await Assert.That(off.HoldsRows).IsFalse();
    }

    /// <summary>
    /// The click the hold was for. A retry wants the poll it nudges, and the pointer left sitting
    /// on the chip it just clicked is no reason to keep the rows frozen.
    /// </summary>
    [Test]
    public async Task TheClickTheHoldWasForLetsTheRowsGo()
    {
        var row = Fixtures.RowOf(Fixtures.WithBuilds(), _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");
        var hovering = Apply(Fixtures.WithBuilds(), new(HoveredChipRow: row, HoveredChip: ChipKind.Retry));
        var clicked = Apply(hovering, new(ClickedChipRow: row, ClickedChip: ChipKind.Retry, HoveredChipRow: row, HoveredChip: ChipKind.Retry));
        await Assert.That(clicked.Hover!.Clicked).IsTrue();
        await Assert.That(clicked.HoldsRows).IsFalse();
    }

    /// <summary>
    /// The pointer moving on to the next button is a hover of its own, so the rows are held again
    /// rather than left free because something was clicked once.
    /// </summary>
    [Test]
    public async Task TheNextButtonHoldsTheRowsAgain()
    {
        var hovering = Apply(Fixtures.WithBuilds(), new(HoveredChipRow: 1, HoveredChip: ChipKind.Retry));
        var clicked = Apply(hovering, new(ClickedChipRow: 1, ClickedChip: ChipKind.Retry, HoveredChipRow: 1, HoveredChip: ChipKind.Retry));
        var next = Apply(clicked, new(HoveredChipRow: 1, HoveredChip: ChipKind.CopyLog));
        await Assert.That(next.HoldsRows).IsTrue();
    }

    /// <summary>
    /// Hiding the window with the pointer on a chip reports no move off it, so a hold left standing
    /// would keep every poll back until it timed out, over a window nobody can see.
    /// </summary>
    [Test]
    public async Task AHiddenWindowHoldsNothing()
    {
        var hovering = Apply(Fixtures.WithBuilds(), new(HoveredChipRow: 1, HoveredChip: ChipKind.Retry));
        await Assert.That(MonitorSession.Hide(hovering).HoldsRows).IsFalse();
    }

    /// <summary>
    /// The head reports the row it drew, which is an index into the visible slice, and the hold is
    /// compared against rows counted from the first, as the selection is.
    /// </summary>
    [Test]
    public async Task TheHoveredRowIsCountedFromTheFirstRow()
    {
        var scrolled = MonitorSession.ScrollTo(Fixtures.WithBuilds(), 2);
        var state = Apply(scrolled, new(HoveredChipRow: 1, HoveredChip: ChipKind.Retry));
        await Assert.That(state.Hover!.Row).IsEqualTo(scrolled.ScrollTop + 1);
    }
}
