/// <summary>
/// What a form page is editing. Field values are strings whatever their widget, which is what
/// lets a head report an edit without knowing the field: the parse happens on save, where a
/// bad value becomes <see cref="Error"/> rather than an exception in a text changed handler.
/// <para>
/// A type per page rather than one record with a member for every page: the filters list was
/// carried by every form and read by one, and the connection editor told adding from editing by
/// which of two ids was null, so a page could be handed another page's data and a save could pick
/// the wrong id. Each page's data is now only on the page it belongs to.
/// </para>
/// </summary>
abstract record FormState
{
    public required ImmutableDictionary<string, string> Values { get; init; }
    public FormError? Error { get; init; }

    /// <summary>
    /// The page this form is, known from its type, so the page returned to after a sign in is the
    /// one the form was opened on rather than a page name written out at the return.
    /// </summary>
    public abstract Page Page { get; }

    public string Value(string id) =>
        CollectionExtensions.GetValueOrDefault(Values, id, "");

    public bool Flag(string id) =>
        Value(id) == "true";

    /// <summary>
    /// The copy is of the runtime type, so a page's own data survives it, but it is typed as the
    /// base: a chain that also sets a page's own member sets that first and calls this last, as a
    /// <c>with</c> only reaches the members of its operand's static type.
    /// </summary>
    public FormState With(string id, string value) =>
        this with
        {
            Values = Values.SetItem(id, value)
        };
}
