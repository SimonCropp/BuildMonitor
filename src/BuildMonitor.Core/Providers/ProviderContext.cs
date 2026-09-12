/// <summary>
/// What a provider call gets: the connection it is for and an HTTP client already carrying the
/// credential.
/// </summary>
record ProviderContext(Connection Connection, HttpJson Http)
{
    public string Scope(string id) =>
        Connection.ScopeValue(id);
}
