/// <summary>
/// The filters page edits a draft list; save swaps it in.
/// </summary>
sealed record FiltersFormState : FormState
{
    public required ImmutableArray<Filter> Filters { get; init; }

    /// <summary>
    /// The deferrals standing when the page opened, less any the user ended here. Listed beside the
    /// filters as the one place a deferral can be ended before it is due.
    /// </summary>
    public ImmutableArray<Deferral> Deferrals { get; init; } = [];

    public override Page Page => Page.Filters;
}
