/// <summary>
/// The page of things that act the moment they are clicked: the version, the documentation, the
/// logs, raising an issue and updating. They were on the options page, whose footer says Save and
/// Cancel, so they read as waiting for a Save, and Update left for its own page and threw away
/// whatever had been typed.
/// <para>
/// Holds the options it was opened from, whole, so Back goes to them with their edits still in
/// them. Only the options open it.
/// </para>
/// </summary>
sealed record AboutFormState : FormState
{
    public required OptionsFormState Options { get; init; }

    public override Page Page => Page.About;
}
