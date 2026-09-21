/// <summary>
/// The open context menu: which visible row it hangs under and what it offers. The commands
/// behind the labels live in <see cref="MenuState"/>; a head only draws the entries, with their
/// lines, and reports an index.
/// </summary>
/// <param name="Overflow">The drop down of the chips the row had no room for. It hangs under that
/// row's overflow chip, where the click was, rather than from the start of the row.</param>
record MenuOverlay(int Row, IReadOnlyList<MenuEntry> Items, bool Overflow = false);
