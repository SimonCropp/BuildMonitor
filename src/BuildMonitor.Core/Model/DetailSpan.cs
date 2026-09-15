/// <summary>
/// One run of a build row's second cell: plain text, or a link a click opens. The cell is split on
/// the managed side, so no head decides for itself which word is the branch, and a pipeline named
/// like a branch cannot send a click to the wrong page.
/// </summary>
/// <param name="Link">What a click on the run opens, as the kind a head reports for it, or
/// <see cref="ChipKind.None"/> for text that opens nothing.</param>
record DetailSpan(string Text, ChipKind Link = ChipKind.None);
