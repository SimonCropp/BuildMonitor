/// <summary>
/// For a head whose toolkit can hold the loop's thread where the loop cannot see it. The loop in
/// <see cref="MonitorProgram"/> runs one frame per <see cref="IHeadWindow{TScreen, TInput}.Present"/>,
/// and on Windows a present pumps messages, which is where user32 runs its own modal loops: for
/// moving or sizing the window, and for a scroll bar from a press to its release. The loop's next
/// step never comes while one runs, so the head is handed those steps to run itself.
/// <para>
/// Optional, and only WinForms takes it. The native heads pump their own events.
/// </para>
/// </summary>
interface IModalFrames
{
    /// <summary>
    /// One frame of the loop, for a head to run on a timer from inside a modal loop: polls the
    /// window, applies what it reports, and returns the screen that results for the head to draw.
    /// Without it the rows froze until the scroll bar was let go, and a resize drew the old screen
    /// into the new size until the mouse came up.
    /// </summary>
    Func<Screen>? Frame { set; }
}
