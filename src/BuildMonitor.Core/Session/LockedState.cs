/// <summary>
/// The desktop session is locked, and what the builds were when it locked. A failure announced
/// while nobody is at the screen is lost in the notification centre or piles up there, one balloon
/// per failure, and some of those builds have gone green again by the time anyone reads them. So
/// nothing is announced while locked, and <see cref="MonitorSession.Unlock"/> announces once what
/// is failing then that was not failing here.
/// </summary>
record LockedState(ImmutableArray<Build> Builds);
