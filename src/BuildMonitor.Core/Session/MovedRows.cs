/// <summary>
/// The visible positions a poll has just changed: each now shows a different build, or the same
/// build in another status, which draws other chips. Running builds are sorted to the top and polled
/// every ten seconds, so a row can move between looking at a chip and clicking it, and the click then
/// lands on another build. Retry, Cancel and Run next change a service's builds, so a click on one
/// of these positions is not taken for them until the change has had time to be seen. See
/// <see cref="MonitorSession.JustMoved"/>.
/// </summary>
/// <param name="ScrollTop">Where the list was scrolled to. A position scrolled since was aimed at
/// afresh.</param>
/// <param name="Positions">Indexes into the visible rows.</param>
record MovedRows(DateTimeOffset At, int ScrollTop, ImmutableHashSet<int> Positions);
