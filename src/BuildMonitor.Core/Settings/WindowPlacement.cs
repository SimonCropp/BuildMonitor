/// <summary>
/// Where the window was left and how big, so it opens there again. It opened centred at its first
/// size on every start, and an app that runs at login and is shown and hidden all day was put back
/// there every morning. The bounds are those it has when not maximized, which is what a restore
/// goes back to.
/// <para>
/// In the units the desktop places windows in, from the top left of the primary screen: pixels on
/// Windows and X11, points on macOS. Each head reads back only what it wrote, as settings.json is
/// per machine.
/// </para>
/// </summary>
/// <param name="Maximized">Whether it filled its screen. A minimized window is saved as it was
/// before, since one that opened minimized would look like one that never opened.</param>
record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized = false);
