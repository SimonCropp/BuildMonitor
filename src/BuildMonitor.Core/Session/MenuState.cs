/// <summary>
/// The open context menu: the row it belongs to and what each label does. A head draws the
/// labels and reports an index; the command behind it is resolved here, so the three heads
/// cannot disagree about what a click meant.
/// </summary>
record MenuState(int Row, ImmutableArray<MenuItem> Items, bool Overflow = false);

record MenuItem(string Label, CommandKind Command);
