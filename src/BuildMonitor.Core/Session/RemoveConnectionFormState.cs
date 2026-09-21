/// <summary>
/// The page that stands between Remove and a connection gone. The editor's Remove sat beside its
/// Cancel and acted on one click, taking the connection and its stored credential with it, and a
/// credential is not always something a user can get back: a token may only ever be shown once.
/// <para>
/// Holds the editor it was asked from, whole, so a no goes back to that editor with any edits
/// still in it, rather than to the builds page, where an answer to "are you sure" does not land.
/// </para>
/// </summary>
sealed record RemoveConnectionFormState : FormState
{
    public required EditConnectionFormState Editor { get; init; }

    public string ConnectionId => Editor.ConnectionId;

    public override Page Page => Page.RemoveConnection;
}
