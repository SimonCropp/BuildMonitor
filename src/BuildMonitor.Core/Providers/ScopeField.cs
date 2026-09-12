/// <summary>
/// One value the provider needs to narrow discovery, entered in the connection editor and kept
/// in <see cref="Connection.Scope"/>.
/// </summary>
record ScopeField(string Id, string Label, bool Required, string? Hint = null);
