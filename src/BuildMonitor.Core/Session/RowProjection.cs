/// <summary>
/// The one rule about which builds are rows and in what order. Every reader of a row index
/// goes through here, so the selection, the menu, the tray and the screen agree.
/// <para>
/// One list across every connection: grouping by connection split what needs attention into as
/// many lists as there were services. Passing builds of one project, or of one prefix named in
/// <see cref="Settings.GroupPrefixes"/>, share a closed group instead,
/// where its most recent member would have been, because a repository with a handful of passing
/// workflows otherwise buries the red and running rows under ones that need nothing. A failure is
/// never grouped: each one wants reading, and a group is a line that hides its members.
/// </para>
/// <para>
/// A pipeline's rows stay together: its own run, then the runs on its other branches that are
/// running, queued or failed, each on the row beneath. Sorted apart, a pull request's run landed
/// anywhere in the list and said nothing about which pipeline it was a run of.
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
        Rows(state, Pipelines(state));

    /// <summary>
    /// The rows from <paramref name="pipelines"/>, which is this state's <see cref="Pipelines"/>,
    /// for a caller that has them already. Sorting them is most of the time a projection takes,
    /// and a screen rebuild used to take it three times, four with a filter typed.
    /// </summary>
    public static ImmutableArray<Row> Rows(SessionState state, ImmutableArray<PipelineBuilds> pipelines)
    {
        if (lastRows is { } last &&
            last.IsFor(state, pipelines))
        {
            return last.Rows;
        }

        var rows = Project(state, pipelines);
        lastRows = new(pipelines, state.Connections, state.Settings.OpenGroups, state.Search, state.Settings.GroupPrefixes, rows);
        return rows;
    }

    static ImmutableArray<Row> Project(SessionState state, ImmutableArray<PipelineBuilds> pipelines)
    {
        // Narrowed before grouping, so a group holds only the members that match: a closed group
        // left whole would hide the one build the filter was typed to find.
        var search = state.Search.Trim();
        var connections = state.Connections.ToDictionary(_ => _.Connection.Id);
        // Each pipeline's group id once, and a key only for a group's row. Making a key for every
        // passing build at every step cost each of them a handful of strings a projection.
        var prefixes = state.Settings.GroupPrefixes;
        var shown = new List<(List<Build> Builds, ImmutableArray<Build> Folded, string? Id)>();
        foreach (var pipeline in pipelines)
        {
            var builds = Matching(pipeline, search);
            if (builds.Count == 0)
            {
                continue;
            }

            // Judged on the whole pipeline rather than on what the search left of it, so a pipeline
            // does not join a group as letters typed hide its lanes. One with a lane is never
            // grouped: its lanes follow its row, and a closed group would hide them with it.
            var id = pipeline is {Lanes.IsEmpty: true, Head: { } head} ? GroupKey.IdOf(head, prefixes) : null;
            shown.Add((builds, pipeline.Folded, id));
        }

        var groups = shown
            .Where(_ => _.Id is not null)
            .GroupBy(_ => _.Id!)
            .Where(_ => _.Count() > 1)
            .ToDictionary(_ => _.Key, _ => _.ToList());
        var rows = ImmutableArray.CreateBuilder<Row>();
        var added = new HashSet<string>();
        foreach (var (builds, folded, id) in shown)
        {
            // A group of one saves nothing and hides that build's links.
            if (id is null ||
                !groups.TryGetValue(id, out var members))
            {
                // The first names the pipeline: its own run, or, where a deferral hid that, its
                // first lane, since a lane under no named row would read as a run of the pipeline
                // above. The branches folded away go with the name.
                for (var index = 0; index < builds.Count; index++)
                {
                    var build = builds[index];
                    if (index == 0)
                    {
                        rows.Add(new(RowKind.Build, connections[build.ConnectionId], build, null, false, [], folded));
                    }
                    else
                    {
                        rows.Add(new(RowKind.Lane, connections[build.ConnectionId], build, null, false, [], []));
                    }
                }

                continue;
            }

            if (!added.Add(id))
            {
                continue;
            }

            var key = GroupKey.Of(builds[0], prefixes)!;
            var expanded = IsExpanded(state, key);
            rows.Add(new(RowKind.Group, null, null, key, expanded, [..members.Select(_ => _.Builds[0])], []));
            if (!expanded)
            {
                continue;
            }

            foreach (var member in members)
            {
                var build = member.Builds[0];
                rows.Add(new(RowKind.Member, connections[build.ConnectionId], build, key, false, [], member.Folded));
            }
        }

        return rows.ToImmutable();
    }

    /// <summary>
    /// What of a pipeline the filter box keeps. A lane that matches keeps its pipeline's own run
    /// above it, since a lane with no named row over it reads as a run of the pipeline above that;
    /// an own run that matches keeps only itself.
    /// </summary>
    static List<Build> Matching(PipelineBuilds pipeline, string search)
    {
        if (search.Length == 0)
        {
            return [..pipeline.Shown];
        }

        var lanes = pipeline.Lanes.Where(_ => Matches(_, search)).ToList();
        if (lanes.Count > 0)
        {
            if (pipeline.Head is { } own)
            {
                lanes.Insert(0, own);
            }

            return lanes;
        }

        if (pipeline.Head is { } head &&
            Matches(head, search))
        {
            return [head];
        }

        return [];
    }

    /// <summary>
    /// A group is closed until someone opens it, and <see cref="Settings.OpenGroups"/> holds the
    /// ones they did.
    /// </summary>
    public static bool IsExpanded(SessionState state, GroupKey key) =>
        state.Settings.OpenGroups.Contains(key.Id);

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
    /// Every connection's builds that get a row, in the order of <see cref="Pipelines"/>. Not
    /// narrowed by the filter box, so the tray, the header's counts and the MCP tools still
    /// describe everything watched while a filter is typed.
    /// </summary>
    public static ImmutableArray<Build> Builds(SessionState state) =>
        Sorted(state).Sorted;

    /// <summary>
    /// Every connection's pipelines: filtered, reduced to the runs worth a row, and sorted so what
    /// is happening now sits at the top.
    /// </summary>
    public static ImmutableArray<PipelineBuilds> Pipelines(SessionState state) =>
        Sorted(state).Pipelines;

    static SortedBuilds Sorted(SessionState state)
    {
        if (lastBuilds is { } last &&
            last.IsFor(state))
        {
            return last;
        }

        var pipelines = Sort(state);
        var sorted = new SortedBuilds(state.Settings, state.Connections, state.Builds, pipelines, [..pipelines.SelectMany(_ => _.Shown)]);
        lastBuilds = sorted;
        return sorted;
    }

    /// <summary>
    /// By each pipeline's most urgent row, the most recent of those where several tie, so a
    /// pipeline whose pull request is running sits with the running rows, its own run above it.
    /// </summary>
    static ImmutableArray<PipelineBuilds> Sort(SessionState state) =>
    [
        ..state.Connections
            .SelectMany(_ => Selected(state, _.Connection.Id))
            .Select(_ => (Pipeline: _, Lead: Lead(_)))
            .OrderBy(_ => _.Lead.Rank())
            .ThenByDescending(_ => _.Lead.Ordering ?? DateTimeOffset.MinValue)
            .ThenBy(_ => _.Lead.PipelineName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(_ => _.Lead.Key, StringComparer.Ordinal)
            .Select(_ => _.Pipeline)
    ];

    static Build Lead(PipelineBuilds pipeline) =>
        pipeline.Shown
            .OrderBy(_ => _.Rank())
            .ThenByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
            .First();

    /// <summary>
    /// A deferral drops a row after the selection rather than before it: dropped first, the run
    /// before the failure would take the row, and a green row would say the pipeline passed. A
    /// deferred lane goes on its own, and a pipeline goes only once nothing of it is left.
    /// </summary>
    static IEnumerable<PipelineBuilds> Selected(SessionState state, string connectionId)
    {
        var selected = BuildSelection.Select(
            Filters.Apply(state.Settings.Filters, state.Builds.Where(_ => _.ConnectionId == connectionId)),
            state.Settings.ShowOtherBranches);
        var deferrals = state.Settings.Deferrals;
        if (deferrals.Length == 0)
        {
            return selected;
        }

        return selected
            .Select(_ => _ with
            {
                Head = _.Head is { } head && Deferrals.Hides(deferrals, head) ? null : _.Head,
                Lanes = _.Lanes.RemoveAll(_ => Deferrals.Hides(deferrals, _))
            })
            .Where(_ => _.Head is not null ||
                        _.Lanes.Length > 0);
    }
}
