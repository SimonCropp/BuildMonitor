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
    /// On whole seconds, because every time on screen is in whole seconds: a quarter second tick
    /// rebuilt, and on Windows repainted, three times in four with nothing new to show.
    /// </summary>
    public static readonly TimeSpan ClockTick = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The tick while the builds page shows its spinner, which on Windows turns only when the canvas
    /// repaints, so on the slower tick it would jump a quarter of a turn at a time.
    /// </summary>
    public static readonly TimeSpan LoadingTick = TimeSpan.FromMilliseconds(250);

    SessionState? builtState;
    DateTimeOffset builtAt;
    Screen? screen;

    /// <summary>
    /// The screen for this frame. <paramref name="rebuilt"/> is false when the previous instance
    /// was returned, so a caller can skip pushing it anywhere.
    /// </summary>
    public Screen Get(SessionState state, DateTimeOffset now, out bool rebuilt)
    {
        rebuilt = screen is null ||
                  !ReferenceEquals(state, builtState) ||
                  Ticked(screen, state, now);
        if (rebuilt)
        {
            screen = ScreenBuilder.Build(state, now);
            builtState = state;
            builtAt = now;
        }

        return screen!;
    }

    /// <summary>
    /// Whether the clock has moved into another tick since <paramref name="built"/>. Never while
    /// hidden: no one sees the screen, and the tray and the notification take no clock, yet a large
    /// account rebuilt it four times a second, about 22 ms of CPU and 15 MB of garbage a second.
    /// Showing the window changes the state, which rebuilds.
    /// </summary>
    bool Ticked(Screen built, SessionState state, DateTimeOffset now)
    {
        if (state.Hidden)
        {
            return false;
        }

        var tick = built.Builds is { Loading: true } ? LoadingTick : ClockTick;
        return now.Ticks / tick.Ticks != builtAt.Ticks / tick.Ticks;
    }
}
