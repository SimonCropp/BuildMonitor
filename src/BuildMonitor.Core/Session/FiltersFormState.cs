/// <summary>
/// The filters page edits a draft list; save swaps it in.
/// </summary>
sealed record FiltersFormState : FormState
{
    public required ImmutableArray<Filter> Filters { get; init; }

    public override Page Page => Page.Filters;
}
