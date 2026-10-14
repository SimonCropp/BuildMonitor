/// <summary>
/// Hands the text waiting in <see cref="SessionState.Clipboard"/> to the window through the
/// kernel's <see cref="ClipboardPump"/>, and lets the state let go of it only once the window has
/// taken it or the pump has given up, saying which.
/// </summary>
static class ClipboardCopy
{
    public static void Offer(ClipboardPump pump, SessionHost host, IMonitorWindow window, PendingCopy copy)
    {
        switch (pump.Offer(copy, copy.Text, window.SetClipboard))
        {
            case ClipboardOffer.Taken:
                host.Mutate(_ => MonitorSession.Copied(_, copy));
                return;
            case ClipboardOffer.GaveUp:
                // The status is read before it is replaced because it is the only name out here for
                // what was being copied: the pump is handed text, and "Copied a triage prompt for X"
                // is what says which build the log just lost belonged to.
                Log.Warning(
                    "Gave up on the clipboard after {Attempts} attempts and {Length} characters. The status was {Status}",
                    pump.Tries,
                    copy.Text.Length,
                    host.State.Status);
                host.Mutate(_ => MonitorSession.CopyFailed(_, copy));
                return;
        }
    }
}
