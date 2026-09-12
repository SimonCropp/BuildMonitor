/// <summary>
/// An exclusion. Anything it matches is neither fetched nor shown.
/// </summary>
record Filter(FilterKind Kind, FilterTarget Target, string Text)
{
    public string Describe() =>
        $"{Target} {Kind.ToString().ToLowerInvariant()} \"{Text}\"";
}
