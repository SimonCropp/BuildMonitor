/// <summary>
/// The connections page: the list, drawn from the session's connections rather than held here,
/// so an editor closing back to it shows what that editor just saved.
/// <para>
/// A page of its own rather than a part of the options: a connection is saved by its own editor
/// the moment that editor's Save is clicked, so on the options page, whose Cancel throws the
/// other changes away, it read as undone by a Cancel that could not touch it.
/// </para>
/// </summary>
sealed record ConnectionsFormState : FormState
{
    public override Page Page => Page.Connections;
}
