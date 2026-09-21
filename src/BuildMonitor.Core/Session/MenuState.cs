/// <summary>
/// The open context menu: the row it belongs to and what each label does. A head draws the
/// labels and reports an index; the command behind it is resolved here, so the three heads
/// cannot disagree about what a click meant.
/// </summary>
record MenuState(int Row, ImmutableArray<MenuItem> Items, bool Overflow = false);

/// <param name="Target">What the command acts on where the row it was opened on does not say it:
/// which of the offered prefixes a Group by prefix item stands for. Null for every item whose
/// command reads the selected row, which is most of them.</param>
record MenuItem(string Label, CommandKind Command, string? Target = null);
