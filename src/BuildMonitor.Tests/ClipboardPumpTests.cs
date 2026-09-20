public class ClipboardPumpTests
{
    const string prompt = "The triage prompt";

    [Test]
    public async Task TextTheWindowTakesIsCleared()
    {
        var window = new FakeWindow();
        var host = Pending(out var text);
        new ClipboardPump().Push(host, window, text);

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
        var host = Pending(out var text);
        new ClipboardPump().Push(host, window, text);

        await Assert.That(host.State.Clipboard).IsEqualTo(prompt);
        await Assert.That(host.State.Status).IsEqualTo("Copied a triage prompt");
    }

    [Test]
    public async Task AClipboardThatFreesUpInTimeStillCopies()
    {
        var window = new FakeWindow { ClipboardBusy = true };
        var host = Pending(out var text);
        var pump = new ClipboardPump();
        pump.Push(host, window, text);
        window.ClipboardBusy = false;
        pump.Push(host, window, text);

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
        var host = Pending(out var text);
        var pump = new ClipboardPump();
        for (var attempt = 0; attempt < ClipboardPump.Attempts; attempt++)
        {
            pump.Push(host, window, text);
        }

        await Assert.That(window.Calls.Count).IsEqualTo(ClipboardPump.Attempts);
        await Assert.That(host.State.Clipboard).IsNull();
        await Assert.That(host.State.Status).IsEqualTo(MonitorSession.ClipboardBusy);
    }

    // A second copy while the first is still being tried is a new text, so it gets its own attempts
    // rather than inheriting a count that would give up on it immediately.
    [Test]
    public async Task ASecondTextStartsItsOwnAttempts()
    {
        var window = new FakeWindow { ClipboardBusy = true };
        var host = Pending(out var text);
        var pump = new ClipboardPump();
        for (var attempt = 0; attempt < ClipboardPump.Attempts - 1; attempt++)
        {
            pump.Push(host, window, text);
        }

        var second = new string("The log".ToCharArray());
        host.Mutate(_ => MonitorSession.Copy(_, second, "Copied the log"));
        pump.Push(host, window, second);

        await Assert.That(host.State.Clipboard).IsEqualTo("The log");
        await Assert.That(host.State.Status).IsEqualTo("Copied the log");
    }

    static SessionHost Pending(out string text)
    {
        // A new instance rather than the literal, because everything here turns on reference
        // equality: the loop clears only the text it was handed.
        text = new(prompt.ToCharArray());
        var host = new SessionHost(Fixtures.WithBuilds());
        var copied = text;
        host.Mutate(_ => MonitorSession.Copy(_, copied, "Copied a triage prompt"));
        return host;
    }
}
