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
    public bool ShowForksAndCollaborations { get; init; }
    public bool NotifyOnFailure { get; init; } = true;
    public Theme Theme { get; init; }
    public int PollIntervalSeconds { get; init; } = 30;
    public int RunningPollIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// How far back a finished build is shown. Older builds are dropped as they arrive rather than
    /// left out of the request, except on GitLab: a service filtering on when a build was created or
    /// queued would also leave out one from before the cutoff that is still running.
    /// </summary>
    public int HistoryDays { get; init; } = 30;
    public int Port { get; init; } = global::Port.Default;

    /// <summary>
    /// Where the user's checkouts live. Every git repository up to two folders below it gets an
    /// open folder button on the rows of the pipelines it builds. Empty when unset, which watches
    /// nothing.
    /// </summary>
    public string CodeDirectory { get; init; } = "";
    /// <summary>
    /// Project name prefixes that group passing builds, ahead of the repository name. A family of
    /// repositories buries the rows that need reading as surely as one repository's workflows do,
    /// and nothing in the names themselves says which repositories are a family. See
    /// <see cref="GroupKey"/>.
    /// </summary>
    public ImmutableArray<string> GroupPrefixes { get; init; } = [];

    /// <summary>
    /// Groups passing builds by the repository's owner, such as a GitHub organisation, where no
    /// prefix and no group of the service's own claims them. Off by default: someone watching one
    /// organisation would see every green row fold into one. See <see cref="GroupKey"/>.
    /// </summary>
    public bool GroupByOrg { get; init; }

    /// <summary>
    /// The <see cref="GroupKey.Id"/> of each group the user opened. A group is closed until they
    /// do. Saved rather than held for the session, which closed every group on each start of an
    /// app that starts at every login.
    /// </summary>
    public ImmutableHashSet<string> OpenGroups { get; init; } = [];

    /// <summary>
    /// Where the window was left. Null until it has first been moved, sized or hidden.
    /// </summary>
    public WindowPlacement? Window { get; init; }
    public ImmutableArray<Connection> Connections { get; init; } = [];
    public ImmutableArray<Filter> Filters { get; init; } = [];

    /// <summary>
    /// Broken builds put off from a row's menu, each until its own time. See <see cref="Deferral"/>.
    /// </summary>
    public ImmutableArray<Deferral> Deferrals { get; init; } = [];
}
