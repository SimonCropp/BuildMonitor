public class ClipboardPumpTests
{
    const string prompt = "The triage prompt";

    [Test]
    public async Task TextTheWindowTakesIsCleared()
    {
        var window = new FakeWindow();
        var host = Pending(out var copy);
        new ClipboardPump().Push(host, window, copy);

        await Assert.That(window.Calls).IsEquivalentTo(["SetClipboard The triage prompt"]);
        await Assert.That(host.State.Clipboard).IsNull();
        await Assert.That(host.State.Status).IsEqualTo("Copied a triage prompt");
    }

    // The bug this class exists for: a busy clipboard used to drop the prompt, which had a log and
    // a bundle of artifacts behind it, and leave the status saying it had been copied.
    [Test]
    public async Task ABusyClipboardKeepsTheTextForTheNextFrame()
    {
        var window = new FakeWindow { ClipboardBusy = true };
        var host = Pending(out var copy);
        new ClipboardPump().Push(host, window, copy);

        await Assert.That(host.State.Clipboard?.Text).IsEqualTo(prompt);
        await Assert.That(host.State.Status).IsEqualTo("Copied a triage prompt");
    }

    [Test]
    public async Task AClipboardThatFreesUpInTimeStillCopies()
    {
        var window = new FakeWindow { ClipboardBusy = true };
        var host = Pending(out var copy);
        var pump = new ClipboardPump();
        pump.Push(host, window, copy);
        window.ClipboardBusy = false;
        pump.Push(host, window, copy);

        await Assert.That(window.Calls.Count).IsEqualTo(2);
        await Assert.That(host.State.Clipboard).IsNull();
        await Assert.That(host.State.Status).IsEqualTo("Copied a triage prompt");
    }

    // Bounded, because a clipboard someone else owns for good would otherwise be asked sixty times
    // a second, each attempt a second long inside the toolkit.
    [Test]
    public async Task AClipboardThatStaysBusyIsGivenUpOnAndSaidSo()
    {
        var window = new FakeWindow { ClipboardBusy = true };
        var host = Pending(out var copy);
        var pump = new ClipboardPump();
        for (var attempt = 0; attempt < ClipboardPump.Attempts; attempt++)
        {
            pump.Push(host, window, copy);
        }

        await Assert.That(window.Calls.Count).IsEqualTo(ClipboardPump.Attempts);
        await Assert.That(host.State.Clipboard).IsNull();
        await Assert.That(host.State.Status).IsEqualTo(MonitorSession.ClipboardBusy);
    }

    // A second copy while the first is still being tried is a new copy, so it gets its own attempts
    // rather than inheriting a count that would give up on it immediately.
    [Test]
    public async Task ASecondTextStartsItsOwnAttempts()
    {
        var window = new FakeWindow { ClipboardBusy = true };
        var host = Pending(out var copy);
        var pump = new ClipboardPump();
        for (var attempt = 0; attempt < ClipboardPump.Attempts - 1; attempt++)
        {
            pump.Push(host, window, copy);
        }

        var second = host.Mutate(_ => MonitorSession.Copy(_, "The log", "Copied the log")).Clipboard!;
        pump.Push(host, window, second);

        await Assert.That(host.State.Clipboard?.Text).IsEqualTo("The log");
        await Assert.That(host.State.Status).IsEqualTo("Copied the log");
    }

    // The notification a triage's copy carries is the whole of what the user in another window
    // hears, so it pops only once the window has taken the text, and says so when it would not.
    [Test]
    public async Task ACopyIsAnnouncedOnlyOnceTheWindowHasTakenIt()
    {
        var window = new FakeWindow { ClipboardBusy = true };
        var host = Triaged(out var copy);
        var pump = new ClipboardPump();
        pump.Push(host, window, copy);
        await Assert.That(host.State.Notification).IsNull();

        window.ClipboardBusy = false;
        pump.Push(host, window, copy);
        await Assert.That(host.State.Notification?.Title).IsEqualTo("Ready to paste");
    }

    [Test]
    public async Task ACopyGivenUpOnIsAnnouncedAsNotCopied()
    {
        var window = new FakeWindow { ClipboardBusy = true };
        var host = Triaged(out var copy);
        var pump = new ClipboardPump();
        for (var attempt = 0; attempt < ClipboardPump.Attempts; attempt++)
        {
            pump.Push(host, window, copy);
        }

        await Assert.That(host.State.Notification?.Title).IsEqualTo("Triage prompt not copied");
        await Assert.That(host.State.Status).IsEqualTo(MonitorSession.ClipboardBusy);
    }

    static SessionHost Pending(out PendingCopy copy)
    {
        var host = new SessionHost(Fixtures.WithBuilds());
        copy = host.Mutate(_ => MonitorSession.Copy(_, prompt, "Copied a triage prompt")).Clipboard!;
        return host;
    }

    static SessionHost Triaged(out PendingCopy copy)
    {
        var state = Fixtures.Triaging();
        var build = state.Triaging.Single();
        var host = new SessionHost(state);
        copy = host.Mutate(_ => MonitorSession.Triaged(_, build, prompt, "Copied a triage prompt")).Clipboard!;
        return host;
    }
}
