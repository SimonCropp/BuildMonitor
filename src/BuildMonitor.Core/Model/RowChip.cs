/// <summary>
/// One button on a build's row, drawn as <paramref name="Icon"/> then <paramref name="Text"/>,
/// either of which may be empty. Both are chosen on the managed side, pull request number and all,
/// so no head spells one differently or picks its own picture for a kind.
/// </summary>
/// <param name="Label">What the drop down calls it, in words. Always words: the drop down opens
/// over the rows and has the room for them, which a row itself does not.</param>
/// <param name="Tooltip">What the button does, said in full. The one thing most chips say, since
/// most of them are only a picture.</param>
/// <param name="Icon">The glyph drawn on the chip, by the name it is registered under, or empty
/// for a chip that is only its text.</param>
/// <param name="Text">What is drawn on the chip, after the icon where there is one. Empty for a
/// chip that is only a picture.</param>
record RowChip(ChipKind Kind, string Label, string Tooltip = "", string Icon = "", string Text = "");
