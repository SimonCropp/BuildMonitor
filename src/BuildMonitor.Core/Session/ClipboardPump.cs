/// <summary>
/// Hands the text waiting in <see cref="SessionState.Clipboard"/> to the window, and lets the state
/// let go of it only once the window has taken it.
/// <para>
/// The loop used to clear it first, on the same reasoning as the notification: something that
/// throws should not be asked again sixty times a second. A clipboard is not like a notification,
/// though. On Windows one process owns it at a time, so a set while a clipboard manager, a remote
/// desktop session or a browser holds it fails outright, and the toolkit's own retry is only about
/// a second long. Clearing first meant those seconds cost the user a triage prompt that had already
/// taken a log and a bundle of artifacts to build, while the status line still said it was copied.
/// </para>
/// </summary>
class ClipboardPump
{
    /// <summary>
    /// How many frames one text gets before the loop gives up on it. Each Windows attempt already
    /// retries inside the toolkit for about a second, so this is a few seconds of a clipboard
    /// someone else is holding, and then a status that says so rather than a silent drop.
    /// </summary>
    public const int Attempts = 3;

    PendingCopy? pending;
    int tries;

    /// <summary>
    /// Offers this frame's copy to the window. <paramref name="copy"/> is compared by reference,
    /// as <see cref="MonitorSession.Copied"/> compares it: a second copy arriving while the first
    /// is still being tried is a new copy, and starts its own attempts.
    /// </summary>
    public void Push(SessionHost host, IMonitorWindow window, PendingCopy copy)
    {
        if (!ReferenceEquals(pending, copy))
        {
            pending = copy;
            tries = 0;
        }

        tries++;
        if (window.SetClipboard(copy.Text))
        {
            pending = null;
            host.Mutate(_ => MonitorSession.Copied(_, copy));
            return;
        }

        if (tries < Attempts)
        {
            return;
        }

        pending = null;
        // The status is read before it is replaced because it is the only name out here for what
        // was being copied: the pump is handed text, and "Copied a triage prompt for X" is what
        // says which build the log just lost belonged to.
        Log.Warning(
            "Gave up on the clipboard after {Attempts} attempts and {Length} characters. The status was {Status}",
            tries,
            copy.Text.Length,
            host.State.Status);
        host.Mutate(_ => MonitorSession.CopyFailed(_, copy));
    }
}
