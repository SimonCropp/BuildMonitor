/// <summary>
/// The one rule about which builds are rows and in what order. Every reader of a row index
/// goes through here, so the selection, the menu, the tray and the screen agree.
/// <para>
/// One list across every connection: grouping by connection split what needs attention into as
/// many lists as there were services. Passing builds of one project share a closed group instead,
/// where its most recent member would have been, because a repository with a handful of passing
/// workflows otherwise buries the red and running rows under ones that need nothing. A failure is
/// never grouped: each one wants reading, and a group is a line that hides its members.
/// </para>
/// <para>
/// The last projection is handed back while a state holds the same instances of what it reads. A
/// poll projected the rows before and after it to keep the selection, and the screen then projected
/// them again, each a sort and a grouping of every build. The state before a poll is the one the
/// screen last drew, and the state a transition returns differs from the one it projected only in
/// what a projection does not read. Compared by reference, the state being immutable, and swapped
/// whole, so a reader on another thread at worst projects again.
/// </para>
/// </summary>
static class RowProjection
{
    static SortedBuilds? lastBuilds;
    static ProjectedRows? lastRows;

    public static ImmutableArray<Row> Rows(SessionState state) =>
        Rows(state, Builds(state));

    /// <summary>
    /// The rows from <paramref name="sorted"/>, which is this state's <see cref="Builds"/>, for a
    /// caller that has them already. <see cref="Builds"/> is most of the time a projection takes,
    /// and a screen rebuild used to take it three times, four with a filter typed.
    /// </summary>
    public static ImmutableArray<Row> Rows(SessionState state, ImmutableArray<Build> sorted)
    {
        if (lastRows is { } last &&
            last.IsFor(state, sorted))
        {
            return last.Rows;
        }

        var rows = Project(state, sorted);
        lastRows = new(sorted, state.Connections, state.OpenGroups, state.Search, rows);
        return rows;
    }

    static ImmutableArray<Row> Project(SessionState state, ImmutableArray<Build> sorted)
    {
        // Narrowed before grouping, so a group holds only the members that match: a closed group
        // left whole would hide the one build the filter was typed to find.
        var search = state.Search.Trim();
        var builds = sorted.Where(_ => Matches(_, search)).ToImmutableArray();
        var connections = state.Connections.ToDictionary(_ => _.Connection.Id);
        // Each build's group id once, and a key only for a group's row. Making a key for every
        // passing build at every step cost each of them a handful of strings a projection.
        var ids = builds.Select(GroupKey.IdOf).ToArray();
        var groups = Enumerable.Range(0, builds.Length)
            .Where(_ => ids[_] is not null)
            .GroupBy(_ => ids[_]!, _ => builds[_])
            .Where(_ => _.Count() > 1)
            .ToDictionary(_ => _.Key, _ => _.ToImmutableArray());
        var rows = ImmutableArray.CreateBuilder<Row>();
        var added = new HashSet<string>();
        for (var index = 0; index < builds.Length; index++)
        {
            var build = builds[index];
            // A group of one saves nothing and hides that build's links.
            if (ids[index] is not { } id ||
                !groups.TryGetValue(id, out var members))
            {
                rows.Add(new(RowKind.Build, connections[build.ConnectionId], build, null, false, []));
                continue;
            }

            if (!added.Add(id))
            {
                continue;
            }

            var key = GroupKey.Of(build)!;
            var expanded = IsExpanded(state, key);
            rows.Add(new(RowKind.Group, null, null, key, expanded, members));
            if (!expanded)
            {
                continue;
            }

            foreach (var member in members)
            {
                rows.Add(new(RowKind.Member, connections[member.ConnectionId], member, key, false, []));
            }
        }

        return rows.ToImmutable();
    }

    /// <summary>
    /// A group is closed until someone opens it, and <see cref="SessionState.OpenGroups"/> holds
    /// the ones they did.
    /// </summary>
    public static bool IsExpanded(SessionState state, GroupKey key) =>
        state.OpenGroups.Contains(key.Id);

    /// <summary>
    /// Whether the build's project, pipeline or branch contains the text, ignoring case. The project
    /// and the branch by the short names a row shows: the full repository name would keep every
    /// repository of an owner whose name holds the text, and a Dependabot branch's full name every
    /// update in an ecosystem, on rows showing nothing that matched.
    /// </summary>
    public static bool Matches(Build build, string search) =>
        search.Length == 0 ||
        BuildExtensions.ShortRepoName(build.RepoName.AsSpan()).Contains(search, StringComparison.OrdinalIgnoreCase) ||
        build.PipelineName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        build.ShortBranchName().Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every connection's builds: filtered, reduced to the runs worth a row, and sorted so what is
    /// happening now sits at the top. Not narrowed by the filter box, so the tray, the header's
    /// counts and the MCP tools still describe everything watched while a filter is typed.
    /// </summary>
    public static ImmutableArray<Build> Builds(SessionState state)
    {
        if (lastBuilds is { } last &&
            last.IsFor(state))
        {
            return last.Sorted;
        }

        var sorted = Sort(state);
        lastBuilds = new(state.Settings, state.Connections, state.Builds, sorted);
        return sorted;
    }

    static ImmutableArray<Build> Sort(SessionState state) =>
    [
        ..state.Connections
            .SelectMany(_ => Selected(state, _.Connection.Id))
            .OrderBy(_ => Rank(_.Status))
            .ThenByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
            .ThenBy(_ => _.PipelineName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(_ => _.Key, StringComparer.Ordinal)
    ];

    static ImmutableArray<Build> Selected(SessionState state, string connectionId) =>
        BuildSelection.Select(
            Filters.Apply(state.Settings.Filters, state.Builds.Where(_ => _.ConnectionId == connectionId)),
            state.Settings.ShowOtherBranches);

    static int Rank(BuildStatus status) =>
        status switch
        {
            BuildStatus.Running => 0,
            BuildStatus.Queued => 1,
            BuildStatus.Failed => 2,
            _ => 3
        };
}
