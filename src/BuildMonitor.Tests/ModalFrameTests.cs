/// <summary>
/// A frame run from inside a head's modal loop, as the scroll bar's or a resize's: the loop's
/// next step never comes while one runs, so without these the window froze until the mouse came up.
/// </summary>
public class ModalFrameTests
{
    /// <summary>
    /// What the window reports is applied as the loop would apply it, and the screen handed back
    /// is the one that results, for the head to draw from inside the modal loop.
    /// </summary>
    [Test]
    public async Task TheWindowsInputIsAppliedAndTheResultingScreenReturned()
    {
        var host = new SessionHost(Fixtures.WithBuilds());
        var window = new FakeWindow
        {
            Next = new(CloseRequested: true)
        };
        var screens = Screens.Cache();

        var screen = MonitorProgram.ModalFrame(host, window, new RecordingActions().Actions, screens);

        await Assert.That(host.State.Hidden).IsTrue();
        await Assert.That(window.Calls).IsEquivalentTo(["SetHidden True"]);
        await Assert.That(screen).IsSameReferenceAs(screens.Get(host.State, DateTimeOffset.UtcNow, out _));
    }
}
