/// <summary>
/// The one rule about which builds are rows and in what order. Every reader of a row index
/// goes through here, so the selection, the menu, the tray and the screen agree.
/// <para>
/// The green builds of one project share a row, where its first member would have been, unless
/// the user expanded it. A repository with a handful of passing workflows otherwise buries the
/// red and running rows under ones that need nothing.
/// </para>
/// </summary>
static class RowProjection
{
    public static ImmutableArray<Row> Rows(SessionState state)
    {
        var rows = ImmutableArray.CreateBuilder<Row>();
        foreach (var connection in state.Connections.OrderBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase))
        {
            var folded = state.FoldedGroups.Contains(connection.Connection.Id);
            rows.Add(new(RowKind.Header, connection, null, folded, []));
            if (folded)
            {
                continue;
            }

            var builds = Builds(state, connection.Connection.Id);
            var collapsed = Collapsible(builds).Except(state.ExpandedProjects);
            var added = new HashSet<string>();
            foreach (var build in builds)
            {
                if (build.Status != BuildStatus.Succeeded ||
                    !collapsed.Contains(build.ProjectKey))
                {
                    rows.Add(new(RowKind.Build, connection, build, false, []));
                    continue;
                }

                if (added.Add(build.ProjectKey))
                {
                    rows.Add(new(
                        RowKind.Project,
                        connection,
                        null,
                        false,
                        [..builds.Where(_ => _.Status == BuildStatus.Succeeded && _.ProjectKey == build.ProjectKey)]));
                }
            }
        }

        return rows.ToImmutable();
    }

    /// <summary>
    /// The projects whose green builds share one row: two or more of them, since a row standing
    /// for a single build saves nothing and hides that build's links.
    /// </summary>
    public static ImmutableHashSet<string> Collapsible(IEnumerable<Build> builds) =>
    [
        ..builds
            .Where(_ => _.Status == BuildStatus.Succeeded)
            .GroupBy(_ => _.ProjectKey)
            .Where(_ => _.Count() > 1)
            .Select(_ => _.Key)
    ];

    /// <summary>
    /// A connection's rows: filtered, reduced to the runs worth a row, and sorted so what is
    /// happening now sits at the top of its group.
    /// </summary>
    public static ImmutableArray<Build> Builds(SessionState state, string connectionId)
    {
        var builds = Filters.Apply(
            state.Settings.Filters,
            state.Builds.Where(_ => _.ConnectionId == connectionId));
        return
        [
            ..BuildSelection.Select(builds, state.Settings.ShowOtherBranches)
                .OrderBy(_ => Rank(_.Status))
                .ThenByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
                .ThenBy(_ => _.PipelineName, StringComparer.OrdinalIgnoreCase)
        ];
    }

    static int Rank(BuildStatus status) =>
        status switch
        {
            BuildStatus.Running => 0,
            BuildStatus.Queued => 1,
            BuildStatus.Failed => 2,
            _ => 3
        };
}
