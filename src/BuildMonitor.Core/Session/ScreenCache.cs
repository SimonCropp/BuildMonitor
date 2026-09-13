/// <summary>
/// Reuses the last <see cref="Screen"/> until the state changes or the clock ticks over.
/// <see cref="Screen"/> holds lists, so record equality is by reference and a head cannot tell
/// an identical rebuild from a change. Rebuilding every frame therefore repainted every row sixty
/// times a second while idle; handing back the same instance lets the head skip the paint.
/// </summary>
class ScreenCache
{
    /// <summary>
    /// How often the clock alone rebuilds the screen, so elapsed times and progress bars still move.
    /// </summary>
    public static readonly TimeSpan ClockTick = TimeSpan.FromMilliseconds(250);

    SessionState? builtState;
    long builtTick = -1;
    Screen? screen;

    /// <summary>
    /// The screen for this frame. <paramref name="rebuilt"/> is false when the previous instance
    /// was returned, so a caller can skip pushing it anywhere.
    /// </summary>
    public Screen Get(SessionState state, DateTimeOffset now, out bool rebuilt)
    {
        var tick = now.Ticks / ClockTick.Ticks;
        rebuilt = screen is null ||
                  !ReferenceEquals(state, builtState) ||
                  tick != builtTick;
        if (rebuilt)
        {
            screen = ScreenBuilder.Build(state, now);
            builtState = state;
            builtTick = tick;
        }

        return screen!;
    }
}
