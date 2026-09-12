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

    public string Scope(string id) =>
        Connection.ScopeValue(id);
}
