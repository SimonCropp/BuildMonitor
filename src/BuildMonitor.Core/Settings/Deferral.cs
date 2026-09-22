/// <summary>
/// A broken build the user has put off until <paramref name="Until"/>: its row, its red on the tray
/// icon and the notification of each run that fails again all wait until then. Keyed by
/// <see cref="Build.Key"/>, the pipeline on its branch, rather than by the run, so a retry that
/// fails the same way does not bring back what the user already said could wait. Saved, as a
/// deferral of days that a restart forgot would be no deferral at all.
/// </summary>
/// <param name="Name">What the filters page and the status line call it, such as "CI failure on
/// main", kept because the build it was made from may no longer be among the rows to ask.</param>
record Deferral(string Key, string Name, DateTimeOffset Until);
