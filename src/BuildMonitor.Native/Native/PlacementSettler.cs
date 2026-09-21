/// <summary>
/// Decides when a native window has settled, from where the library says it is on every poll. The
/// libraries report where the window is rather than when it stopped moving, as raylib has no event
/// for the end of a drag, and each placement reported is a save: passed on as it came, a drag across
/// the screen would write the settings sixty times a second.
/// <para>
/// A placement is passed on once it has held for half a second, or at once when the window is
/// hidden, which is how it is left every time. The first one seen is where the window opened, which
/// is where it was left or a first start, and neither is news.
/// </para>
/// </summary>
sealed class PlacementSettler
{
    static TimeSpan settle = TimeSpan.FromMilliseconds(500);

    WindowPlacement? seen;
    TimeSpan changedAt;
    bool pending;
    bool hidden;

    /// <param name="current">Where the window is, or null where the library does not know, as
    /// before the window is first shown.</param>
    /// <param name="now">Time on any clock that only goes forward.</param>
    public WindowPlacement? Poll(WindowPlacement? current, TimeSpan now)
    {
        if (current is not null &&
            current != seen)
        {
            pending = seen is not null;
            seen = current;
            changedAt = now;
        }

        if (!pending ||
            (!hidden && now - changedAt < settle))
        {
            hidden = false;
            return null;
        }

        pending = false;
        hidden = false;
        return seen;
    }

    public void Hidden() =>
        hidden = true;
}
