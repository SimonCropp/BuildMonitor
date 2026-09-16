/// <summary>
/// The one rule about which builds are rows and in what order. Every reader of a row index
/// goes through here, so the selection, the menu, the tray and the screen agree.
/// <para>
/// One list across every connection: grouping by connection split what needs attention into as
/// many lists as there were services. Finished builds of one project share a group instead, where
/// its most recent member would have been. Failures default open, because each one wants reading;
/// passes default closed, because a repository with a handful of passing workflows otherwise
/// buries the red and running rows under ones that need nothing.
/// </para>
/// </summary>
static class RowProjection
{
    public static ImmutableArray<Row> Rows(SessionState state) =>
        Rows(state, Builds(state));

    /// <summary>
    /// The rows from <paramref name="sorted"/>, which is this state's <see cref="Builds"/>, for a
    /// caller that has them already. <see cref="Builds"/> is most of the time a projection takes,
    /// and a screen rebuild used to take it three times, four with a filter typed.
    /// </summary>
    public static ImmutableArray<Row> Rows(SessionState state, ImmutableArray<Build> sorted)
    {
        // Narrowed before grouping, so a group holds only the members that match: a closed group
        // left whole would hide the one build the filter was typed to find.
        var search = state.Search.Trim();
        var builds = sorted.Where(_ => Matches(_, search)).ToImmutableArray();
        var connections = state.Connections.ToDictionary(_ => _.Connection.Id);
        var groups = builds
            .Where(_ => GroupKey.Of(_) is not null)
            .GroupBy(_ => GroupKey.Of(_)!.Id)
            .Where(_ => _.Count() > 1)
            .ToDictionary(_ => _.Key, _ => _.ToImmutableArray());
        var rows = ImmutableArray.CreateBuilder<Row>();
        var added = new HashSet<string>();
        foreach (var build in builds)
        {
            // A group of one saves nothing and hides that build's links.
            if (GroupKey.Of(build) is not { } key ||
                !groups.TryGetValue(key.Id, out var members))
            {
                rows.Add(new(RowKind.Build, connections[build.ConnectionId], build, null, false, []));
                continue;
            }

            if (!added.Add(key.Id))
            {
                continue;
            }

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
    /// <see cref="SessionState.ToggledGroups"/> holds the groups flipped from their default.
    /// Storing the open ones instead would leave a project that starts failing closed until
    /// someone opened it.
    /// </summary>
    public static bool IsExpanded(SessionState state, GroupKey key) =>
        key.Failed != state.ToggledGroups.Contains(key.Id);

    /// <summary>
    /// Whether the build's project, pipeline or branch contains the text, ignoring case. The project
    /// and the branch by the short names a row shows: the full repository name would keep every
    /// repository of an owner whose name holds the text, and a Dependabot branch's full name every
    /// update in an ecosystem, on rows showing nothing that matched.
    /// </summary>
    public static bool Matches(Build build, string search) =>
        search.Length == 0 ||
        build.ShortRepoName().Contains(search, StringComparison.OrdinalIgnoreCase) ||
        build.PipelineName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        build.ShortBranchName().Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every connection's builds: filtered, reduced to the runs worth a row, and sorted so what is
    /// happening now sits at the top. Not narrowed by the filter box, so the tray, the header's
    /// counts and the MCP tools still describe everything watched while a filter is typed.
    /// </summary>
    public static ImmutableArray<Build> Builds(SessionState state) =>
    [
        ..state.Connections
            .SelectMany(_ => Selected(state, _.Connection.Id))
            .OrderBy(_ => Rank(_.Status))
            .ThenByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
            .ThenBy(_ => _.PipelineName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(_ => _.Key, StringComparer.Ordinal)
    ];

    static IEnumerable<Build> Selected(SessionState state, string connectionId) =>
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
