/// <summary>
/// What a form page is editing. Field values are strings whatever their widget, which is what
/// lets a head report an edit without knowing the field: the parse happens on save, where a
/// bad value becomes <see cref="Error"/> rather than an exception in a text changed handler.
/// </summary>
/// <param name="Filters">The filters page edits a draft list; save swaps it in.</param>
/// <param name="EditingConnectionId">The connection an editor page opened on, or null for a new one.</param>
/// <param name="DraftConnectionId">The id a new connection will have. Assigned when the editor
/// opens so a browser sign in can store its token before the connection is saved.</param>
/// <param name="Message">A result line: a test outcome, who signed in.</param>
record FormState(
    Page Page,
    ImmutableDictionary<string, string> Values,
    ImmutableArray<Filter> Filters,
    string? Error,
    string? EditingConnectionId,
    string? DraftConnectionId,
    string? Message,
    bool SignedIn)
{
    public string Value(string id) =>
        Values.TryGetValue(id, out var value) ? value : "";

    public bool Flag(string id) =>
        Value(id) == "true";

    public FormState With(string id, string value) =>
        this with { Values = Values.SetItem(id, value) };
}
