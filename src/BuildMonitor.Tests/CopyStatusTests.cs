public class CopyStatusTests
{
    // Through Apply, as a click arrives: Apply clears the status for any input, which once ran
    // before the copy and put the fallback, "Polled 3s ago", on the clipboard instead.
    [Test]
    public async Task ClickCopiesTheStatusAndLeavesIt()
    {
        var failure = "Retrying Invitation Event Publisher failed: 400 Bad Request";
        var state = MonitorSession.SetStatus(Fixtures.WithBuilds(), failure);
        state = InputApplier.Apply(state, new(Key: CommandKind.CopyStatus), new RecordingActions().Actions, new FakeWindow());
        await Assert.That(state.Clipboard).IsEqualTo(failure);
        await Assert.That(state.Status).IsEqualTo(failure);
    }
}
