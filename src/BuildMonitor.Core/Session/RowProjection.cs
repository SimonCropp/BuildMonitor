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
    public static ImmutableArray<Row> Rows(SessionState state)
    {
        var builds = Builds(state);
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
    /// Every connection's builds: filtered, reduced to the runs worth a row, and sorted so what is
    /// happening now sits at the top.
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
