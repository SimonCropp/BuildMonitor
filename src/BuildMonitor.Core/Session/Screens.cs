/// <summary>
/// How the clock rebuilds this app's screen, for the <see cref="ScreenCache"/> the loop keeps.
/// Every time on screen is in whole seconds and moves with the clock, so the cache rebuilds the
/// screen once a second as well as for every new state.
/// </summary>
static class Screens
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

    public static ScreenCache Cache() =>
        new(ScreenBuilder.Build, Tick);

    /// <summary>
    /// Never while <see cref="SessionState.Unseen"/>: no one sees the screen, and the tray and the
    /// notification take no clock, yet a large account rebuilt it four times a second, about 22 ms
    /// of CPU and 15 MB of garbage a second. Showing, restoring or unlocking changes the state,
    /// which rebuilds.
    /// </summary>
    static TimeSpan? Tick(SessionState state, Screen screen)
    {
        if (state.Unseen)
        {
            return null;
        }

        if (screen.Builds is { Loading: true })
        {
            return LoadingTick;
        }

        return ClockTick;
    }
}
