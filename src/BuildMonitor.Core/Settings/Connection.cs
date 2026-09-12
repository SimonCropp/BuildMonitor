/// <summary>
/// One configured CI service. <see cref="Scope"/> holds whatever the provider needs to narrow
/// discovery: an organization, a workspace, a project, keyed by <see cref="ScopeField.Id"/>.
/// </summary>
record Connection
{
    public required string Id { get; init; }
    public required string ProviderId { get; init; }
    public required string Name { get; init; }
    public string? Server { get; init; }
    public ImmutableDictionary<string, string> Scope { get; init; } = ImmutableDictionary<string, string>.Empty;
    public AuthMethod Auth { get; init; }
    public string? User { get; init; }
    // A user supplied OAuth client id, for a self hosted instance with its own application
    // registration. Null means the id compiled into OAuthClients.
    public string? ClientId { get; init; }
    // The loopback port the browser sign in listens on. Null picks a free one, which every
    // hosted service accepts; a self hosted server whose application was registered with a
    // fixed redirect port needs that port here.
    public int? CallbackPort { get; init; }

    public string ScopeValue(string id) =>
        Scope.TryGetValue(id, out var value) ? value : "";
}
