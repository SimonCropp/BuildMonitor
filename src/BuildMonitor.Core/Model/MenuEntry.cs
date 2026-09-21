/// <summary>
/// One item of the open context menu as a head draws it: its label, and whether a line goes above
/// it. A line on an item rather than an item of its own, so the index a head reports for a click
/// is still the index of the command behind it, with no separators to count past.
/// </summary>
record MenuEntry(string Label, bool SeparatorAbove = false);
