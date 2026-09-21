/// <summary>
/// What the two connection editors share: the fields below the provider, and the id a credential
/// is stored under. The id is fixed when the page opens rather than picked at save from whichever
/// of two nullable ids was set, which could store one connection's token under another's id.
/// </summary>
abstract record ConnectionFormState : FormState
{
    /// <summary>
    /// A new connection's draft id, or the id of the connection being edited. Known before the
    /// save so a browser sign in can store its token before the connection exists.
    /// </summary>
    public required string ConnectionId { get; init; }

    /// <summary>
    /// A credential is stored under <see cref="ConnectionId"/>: always for a connection being
    /// edited, and for a new one once its sign in has finished.
    /// </summary>
    public bool SignedIn { get; init; }

    /// <summary>
    /// A result line: a test outcome, who signed in.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// The options page this editor was opened from, whole and as it was left, which closing the
    /// editor goes back to. Null where it was opened from anywhere else. Every way out used to go
    /// to the builds page, so adding a second connection meant opening the options again, and the
    /// options opened again from the saved settings, losing anything typed on them first.
    /// </summary>
    public OptionsFormState? Options { get; init; }

    public abstract ProviderDescriptor Descriptor { get; }
}
