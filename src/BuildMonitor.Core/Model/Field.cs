/// <summary>
/// One widget of a form page. A head keys its controls by <see cref="Id"/>, rebuilds them only
/// when the sequence of ids changes, and reports edits as a <see cref="FieldChange"/>.
/// </summary>
/// <param name="Value">A checkbox carries "true"/"false", a select carries the chosen option,
/// a label carries its text.</param>
/// <param name="Hint">What to type while the box is empty, drawn inside it as every toolkit's
/// placeholder. Gone as soon as there is a value, so this is only for a field whose hint stops
/// being worth reading once it is filled in.</param>
/// <param name="Note">A standing fact about the field, drawn beside the box whatever it holds.
/// Separate from <see cref="Hint"/> because a placeholder on a box that is never empty is never
/// read: a number field always carries a value, so the sentence saying a port only takes effect
/// after a restart was written, passed to the head, and shown to nobody.</param>
record Field(
    string Id,
    FieldKind Kind,
    string Label,
    string Value,
    bool Enabled = true,
    IReadOnlyList<string>? Options = null,
    string? Hint = null,
    string? Note = null);
