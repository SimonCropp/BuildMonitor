/// <summary>
/// A triage from the click to the tray. The prompt only reaches the clipboard once the download is
/// done, so the row shows it is still going, the footer keeps saying so, and the tray speaks only
/// once the prompt is really there: whoever clicked has usually gone to another window to paste.
/// </summary>
public class TriageTests
{
    [Test]
    public async Task TheChipIsBusyWhileItCollects()
    {
        var state = Fixtures.Triaging();
        var chip = Chip(state);
        await Assert.That(chip.Icon).IsEqualTo("busy");
        await Assert.That(chip.Label).IsEqualTo("Triaging");
        // The same kind, so it keeps its place on the row and a click on it still reaches the
        // applier, which is what turns the click down.
        await Assert.That(chip.Kind).IsEqualTo(ChipKind.Triage);
    }

    /// <summary>
    /// A retry puts a new run on the row, which is a triage of its own. The busy chip belongs to
    /// the run before it.
    /// </summary>
    [Test]
    public async Task ANewRunOnTheRowIsNotBusy()
    {
        var state = Fixtures.Triaging();
        var collecting = state.Triaging.Single();
        await Assert.That(MonitorSession.IsTriaging(state, collecting)).IsTrue();
        await Assert.That(MonitorSession.IsTriaging(
            state,
            collecting
                with
                {
                    RunNumber = "78"
                }))
            .IsFalse();
    }

    [Test]
    public async Task SeveralCollectingAreCounted()
    {
        var state = Fixtures.Triaging();
        var other = state.Builds.First(_ => _.Status == BuildStatus.Running);
        state = MonitorSession.StartTriage(state, other);
        await Assert.That(ScreenBuilder.Status(state, Fixtures.Now)).IsEqualTo("Collecting 2 builds for triage");
    }

    /// <summary>
    /// The message of whatever the user did since comes first, as it does over the standing
    /// problems, and the triage is back once the next click has cleared it.
    /// </summary>
    [Test]
    public async Task ANewerMessageComesFirst()
    {
        var state = MonitorSession.SetStatus(Fixtures.Triaging(), "Opened /code/DiffEngine");
        await Assert.That(ScreenBuilder.Status(state, Fixtures.Now)).IsEqualTo("Opened /code/DiffEngine");
    }

    /// <summary>
    /// The chip is back, and the prompt waits for the window, but nothing is announced yet: until
    /// the window takes it, the clipboard still holds whatever was copied before.
    /// </summary>
    [Test]
    public async Task ThePromptIsAnnouncedOnlyOnceTheWindowTakesIt()
    {
        var state = Fixtures.Triaging();
        var build = state.Triaging.Single();
        var landed = MonitorSession.Triaged(state, build, "the prompt", "Copied a triage prompt for test.yml #77");
        await Assert.That(landed.Triaging).IsEmpty();
        await Assert.That(Chip(landed).Icon).IsEqualTo("triage");
        await Assert.That(landed.Clipboard?.Text).IsEqualTo("the prompt");
        await Assert.That(landed.Notification).IsNull();

        var copied = MonitorSession.Copied(landed, landed.Clipboard!);
        await Assert.That(copied.Notification).IsEqualTo(new("Ready to paste", "The triage prompt for test.yml #77 is on the clipboard.", build.Key, NotificationKind.Info));
    }

    [Test]
    public async Task APromptTheWindowWouldNotTakeIsAnnouncedAsNotCopied()
    {
        var state = Fixtures.Triaging();
        var build = state.Triaging.Single();
        var landed = MonitorSession.Triaged(state, build, "the prompt", "Copied a triage prompt for test.yml #77");
        var failed = MonitorSession.CopyFailed(landed, landed.Clipboard!);
        await Assert.That(failed.Status).IsEqualTo(MonitorSession.ClipboardBusy);
        await Assert.That(failed.Notification).IsEqualTo(new("Triage prompt not copied", "Another app is holding the clipboard. Try again.", build.Key));
    }

    /// <summary>
    /// A log that lands between the prompt being composed and the window taking it replaces the
    /// prompt on the clipboard, and must not then be announced as the prompt.
    /// </summary>
    [Test]
    public async Task ACopyThatOvertakesThePromptIsNotAnnouncedAsIt()
    {
        var state = Fixtures.Triaging();
        var landed = MonitorSession.Triaged(state, state.Triaging.Single(), "the prompt", "Copied a triage prompt for test.yml #77");
        var overtaken = MonitorSession.Copy(landed, "the log", "Copied the log of test.yml #77");
        var copied = MonitorSession.Copied(overtaken, overtaken.Clipboard!);
        await Assert.That(copied.Notification).IsNull();
    }

    [Test]
    public async Task AFailedTriageIsAnnounced()
    {
        var state = Fixtures.Triaging();
        var build = state.Triaging.Single();
        const string status = "Triaging test.yml #77 failed: 502 Bad Gateway";
        var failed = MonitorSession.TriageFailed(state, build, status);
        await Assert.That(failed.Triaging).IsEmpty();
        await Assert.That(failed.Status).IsEqualTo(status);
        await Assert.That(failed.Clipboard).IsNull();
        await Assert.That(failed.Notification).IsEqualTo(new("Triage failed", status, build.Key));
    }

    static RowChip Chip(SessionState state)
    {
        var row = Fixtures.RowOf(state, _ => _.Build?.Key == "gh/Verify/test.yml/feature/inline");
        return ScreenBuilder.Build(state, Fixtures.Now)
            .Builds!
            .Rows[row]
            .Chips
            .Single(_ => _.Kind == ChipKind.Triage);
    }
}
