/// <summary>
/// Everything persisted in settings.json. Immutable so it can sit inside <see cref="SessionState"/>;
/// the options page edits a copy and swaps it in on save.
/// <para>
/// Carries no secrets. Tokens live in the platform secret store under
/// <see cref="SecretKeys.Token"/>, keyed by <see cref="Connection.Id"/>.
/// </para>
/// </summary>
record Settings
{
    public bool RunAtStartup { get; init; }
    public bool ShowWindowAtStart { get; init; } = true;
    public bool ShowOtherBranches { get; init; } = true;
    public bool NotifyOnFailure { get; init; } = true;
    public int PollIntervalSeconds { get; init; } = 30;
    public int RunningPollIntervalSeconds { get; init; } = 10;
    public int Port { get; init; } = global::Port.Default;
    public ImmutableArray<Connection> Connections { get; init; } = [];
    public ImmutableArray<Filter> Filters { get; init; } = [];
}
