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

    public string Scope(string id) =>
        Connection.ScopeValue(id);
}
