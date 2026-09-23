/// <summary>
/// The button of a row the pointer is on, as the head reports it every frame. Polling holds the
/// rows still while one is here, so a poll cannot re-sort them out from under a pointer already on
/// its way to a chip. See <see cref="SessionState.HoldsRows"/>.
/// </summary>
/// <param name="Row">The row it belongs to, counted from the first row rather than from the top of
/// the window, as <see cref="SessionState.SelectedRow"/> is.</param>
/// <param name="Clicked">Whether the click the hold was for has happened. The rows are let go at
/// once: a retry or a cancel wants the poll it nudges, and a pointer resting on the chip it just
/// clicked is no reason to keep them frozen. Moving to another button holds them again.</param>
record HoverState(int Row, ChipKind Chip, bool Clicked = false);
