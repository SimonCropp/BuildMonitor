/// <summary>
/// The one rule about which builds are rows and in what order. Every reader of a row index
/// goes through here, so the selection, the menu, the tray and the screen agree.
/// </summary>
static class RowProjection
{
    public static ImmutableArray<Row> Rows(SessionState state)
    {
        var rows = ImmutableArray.CreateBuilder<Row>();
        foreach (var connection in state.Connections.OrderBy(_ => _.Connection.Name, StringComparer.OrdinalIgnoreCase))
        {
            var folded = state.FoldedGroups.Contains(connection.Connection.Id);
            rows.Add(new(RowKind.Header, connection, null, folded));
            if (folded)
            {
                continue;
            }

            foreach (var build in Builds(state, connection.Connection.Id))
            {
                rows.Add(new(RowKind.Build, connection, build, false));
            }
        }

        return rows.ToImmutable();
    }

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
