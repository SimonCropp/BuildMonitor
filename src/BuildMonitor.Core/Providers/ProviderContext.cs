/// <summary>
/// What a provider call gets: the connection it is for and an HTTP client already carrying the
/// credential.
/// </summary>
record ProviderContext(Connection Connection, HttpJson Http)
{
    /// <summary>
    /// Called by a provider that walks many items, after each one, with (done, total). The
    /// default drops it; the poller sets one that shows it on the header row.
    /// </summary>
    public Action<PollProgress> Progress { get; init; } = _ => { };

    /// <summary>
    /// Whether discovery includes forks and repositories the user only collaborates on. Off, they
    /// are left out of the listing itself, so an account with hundreds of forks pays no workflow
    /// call per fork only to hide the result.
    /// </summary>
    public bool ShowForksAndCollaborations { get; init; }

    /// <summary>
    /// The oldest a build may be to be asked for, or null for no limit. Set only when polling, to
    /// the start of a UTC day by <see cref="HistoryCutoff"/>, so a URL carrying it keeps its cached
    /// ETag all day. Sent only where the service filters on when a build last changed; a filter on
    /// when it was created or queued would leave out a build from before the cutoff that is still
    /// running or was re-run. Other providers ignore it, and the older builds are dropped as they arrive.
    /// </summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>
    /// What the provider remembers about this connection between polls. The poller keeps one for
    /// as long as it polls the connection; any other call starts with an empty one.
    /// </summary>
    public ProviderMemory Memory { get; init; } = new();

    /// <summary>
    /// What each user id is called, shared by every connection the poller runs, so that a provider
    /// handed an id by another service can name it. Any other call starts with an empty one, which
    /// names nobody.
    /// </summary>
    public IdentityNames Identities { get; init; } = new();

    /// <summary>
    /// What the credential may do to builds, as <see cref="IProvider.Access"/> last said. Set only
    /// when polling, for a provider whose discovery narrows it by what the user may do to each
    /// pipeline; any other call leaves it unknown.
    /// </summary>
    public BuildAccess Access { get; init; }

    public string Scope(string id) =>
        Connection.ScopeValue(id);
}
