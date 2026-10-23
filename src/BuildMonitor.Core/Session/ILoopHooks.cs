/// <summary>
/// For a head whose toolkit can hold the loop's thread where the loop cannot see it. The loop in
/// <see cref="MonitorProgram"/> runs one frame per <see cref="IHeadWindow{TScreen, TInput}.Present"/>,
/// and on Windows a present pumps messages, which is where user32 runs its own modal loops, for
/// moving or sizing the window and for a scroll bar from a press to its release, and where a logoff
/// ends the session. The loop's next step never comes in either case, so the head is handed those
/// steps to run itself.
/// <para>
/// Optional, and only WinForms takes it. The native heads pump their own events, and the macOS one
/// turns a logoff's quit into the app's own (TerminateGuard), which the loop leaves by as usual.
/// </para>
/// </summary>
interface ILoopHooks
{
    /// <summary>
    /// One frame of the loop, for a head to run on a timer from inside a modal loop: polls the
    /// window, applies what it reports, and returns the screen that results for the head to draw.
    /// Without it the rows froze until the scroll bar was let go, and a resize drew the old screen
    /// into the new size until the mouse came up.
    /// </summary>
    Func<Screen>? Frame { set; }

    /// <summary>
    /// The session is ending: save what only a clean exit would, before returning. Windows may end
    /// the process as soon as a window has answered the message that says so, which is before the
    /// loop would get as far as its own shutdown.
    /// </summary>
    Action? SessionEnding { set; }
}
