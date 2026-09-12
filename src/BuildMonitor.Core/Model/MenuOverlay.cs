/// <summary>
/// The open context menu: which visible row it hangs under and what it offers. The commands
/// behind the labels live in <see cref="MenuState"/>; a head only draws labels and reports an
/// index.
/// </summary>
record MenuOverlay(int Row, IReadOnlyList<string> Labels);
