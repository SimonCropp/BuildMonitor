/// <summary>
/// One widget of a form page. A head keys its controls by <see cref="Id"/>, rebuilds them only
/// when the sequence of ids changes, and reports edits as a <see cref="FieldChange"/>.
/// </summary>
/// <param name="Value">A checkbox carries "true"/"false", a select carries the chosen option,
/// a label carries its text.</param>
/// <param name="Command">What a Button, Link or ListRow does when clicked.</param>
record Field(
    string Id,
    FieldKind Kind,
    string Label,
    string Value,
    bool Enabled = true,
    IReadOnlyList<string>? Options = null,
    string? Hint = null,
    CommandKind Command = CommandKind.None);
