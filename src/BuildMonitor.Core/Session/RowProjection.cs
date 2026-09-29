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
    public static ImmutableArray<Row> Rows(SessionState state, ImmutableArray<PipelineBuilds> pipelines) =>
        Projected(state, pipelines).Rows;

    /// <summary>
    /// The rows with what they were projected from, for a caller that keeps something read from
    /// them for as long as they stand.
    /// </summary>
    public static ProjectedRows Projected(SessionState state, ImmutableArray<PipelineBuilds> pipelines)
    {
        if (lastRows is { } last &&
            last.IsFor(state, pipelines))
        {
            return last;
        }

        var projected = new ProjectedRows(pipelines, state.Connections, state.Settings.OpenGroups, state.Search, state.Settings.GroupPrefixes, state.Settings.GroupByOrg, Project(state, pipelines));
        lastRows = projected;
        return projected;
    }

    static ImmutableArray<Row> Project(SessionState state, ImmutableArray<PipelineBuilds> pipelines)
    {
        // Narrowed before grouping, so a group holds only the members that match: a closed group
        // left whole would hide the one build the filter was typed to find.
        var search = state.Search.Trim();
        var builds = Sorted(state).Sorted;
        if (search.Length > 0)
        {
            builds = [..builds.Where(_ => Matches(_, search))];
        }

        // What each pipeline's own run folds away, for its hover. By reference, since two runs can
        // be equal as records.
        var folded = new Dictionary<Build, ImmutableArray<FoldedBranch>>(ReferenceEqualityComparer.Instance);
        foreach (var pipeline in pipelines)
        {
            if (pipeline.Head is { } head)
            {
                folded[head] = pipeline.Folded;
            }
        }

        var connections = state.Connections.ToDictionary(_ => _.Connection.Id);
        // Each build's group id once, and a key only for a group's row. Making a key for every
        // passing build at every step cost each of them a handful of strings a projection.
        var prefixes = state.Settings.GroupPrefixes;
        var byOrg = state.Settings.GroupByOrg;
        var ids = new string?[builds.Length];
        var sharing = new Dictionary<string, List<Build>>();
        for (var index = 0; index < builds.Length; index++)
        {
            if (GroupKey.IdOf(builds[index], prefixes, byOrg) is not { } id)
            {
                continue;
            }

            ids[index] = id;
            if (!sharing.TryGetValue(id, out var sharers))
            {
                sharers = [];
                sharing[id] = sharers;
            }

            sharers.Add(builds[index]);
        }

        var groups = new Dictionary<string, ImmutableArray<Build>>();
        foreach (var (id, sharers) in sharing)
        {
            if (sharers.Count > 1)
            {
                groups[id] = ByRepository(sharers);
            }
        }

        var rows = ImmutableArray.CreateBuilder<Row>();
        var added = new HashSet<string>();
        for (var index = 0; index < builds.Length; index++)
        {
            var build = builds[index];
            // A group of one saves nothing and hides that build's links.
            if (ids[index] is not { } id ||
                !groups.TryGetValue(id, out var members))
            {
                rows.Add(new(RowKind.Build, connections[build.ConnectionId], build, null, false, [], FoldedOf(folded, build)));
                continue;
            }

            if (!added.Add(id))
            {
                continue;
            }

            var key = GroupKey.Name(members, prefixes, byOrg);
            var expanded = IsExpanded(state, key);
            rows.Add(new(RowKind.Group, null, null, key, expanded, members, []));
            if (!expanded)
            {
                continue;
            }

            string? above = null;
            foreach (var member in members)
            {
                var namedAbove = string.Equals(above, member.RepoName, StringComparison.OrdinalIgnoreCase);
                rows.Add(new(RowKind.Member, connections[member.ConnectionId], member, key, false, [], FoldedOf(folded, member), namedAbove));
                above = member.RepoName;
            }
        }

        return rows.ToImmutable();
    }

    /// <summary>
    /// A repository's pipelines together, in the order its most recent one came, so under a prefix
    /// group each repository is named once, on the first of its rows. Most groups are one
    /// repository's workflows, which are together as they stand.
    /// </summary>
    static ImmutableArray<Build> ByRepository(List<Build> builds)
    {
        var together = true;
        foreach (var build in builds)
        {
            if (!string.Equals(build.RepoName, builds[0].RepoName, StringComparison.OrdinalIgnoreCase))
            {
                together = false;
                break;
            }
        }

        if (together)
        {
            return [..builds];
        }

        return
        [
            ..builds
                .GroupBy(_ => _.RepoName, StringComparer.OrdinalIgnoreCase)
                .SelectMany(_ => _)
        ];
    }

    static ImmutableArray<FoldedBranch> FoldedOf(Dictionary<Build, ImmutableArray<FoldedBranch>> folded, Build build)
    {
        if (folded.TryGetValue(build, out var branches))
        {
            return branches;
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
    /// Every connection's builds that get a row, each sorted by its own status: running, queued,
    /// failed, then the rest by age. A pipeline's other branches sort among the rest rather than
    /// under their pipeline's own run, which put a failed pull request under a green main, and a
    /// green main up among the running rows. Not narrowed by the filter box, so the tray, the
    /// header's counts and the MCP tools still describe everything watched while a filter is typed.
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
        ImmutableArray<Build> builds =
        [
            ..pipelines
                .SelectMany(_ => _.Shown)
                .OrderBy(_ => _.Rank())
                .ThenByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
                .ThenBy(_ => _.PipelineName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(_ => _, Build.ByKey)
        ];
        var sorted = new SortedBuilds(state.Settings, state.Connections, state.Builds, state.Verdicts, pipelines, builds);
        lastBuilds = sorted;
        return sorted;
    }

    /// <summary>
    /// By each pipeline's own run, for the readers that walk pipelines rather than rows.
    /// </summary>
    static ImmutableArray<PipelineBuilds> Sort(SessionState state)
    {
        var askable = BranchHosts.Askable(state.Connections.Select(_ => _.Connection));
        return
        [
            ..state.Connections
                .SelectMany(_ => Selected(state, _.Connection.Id, askable))
                .Select(_ => (Pipeline: _, Lead: Lead(_)))
                .OrderBy(_ => _.Lead.Rank())
                .ThenByDescending(_ => _.Lead.Ordering ?? DateTimeOffset.MinValue)
                .ThenBy(_ => _.Lead.PipelineName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(_ => _.Lead, Build.ByKey)
                .Select(_ => _.Pipeline)
        ];
    }

    /// <summary>
    /// The row a pipeline sorts by: its own run, or, where a deferral hid that, the first lane,
    /// which names the pipeline in its place.
    /// </summary>
    static Build Lead(PipelineBuilds pipeline) =>
        pipeline.Head ?? pipeline.Lanes[0];

    /// <summary>
    /// A deferral drops a row after the selection rather than before it: dropped first, the run
    /// before the failure would take the row, and a green row would say the pipeline passed. A
    /// deferred lane goes on its own, and a pipeline goes only once nothing of it is left.
    /// </summary>
    static IEnumerable<PipelineBuilds> Selected(SessionState state, string connectionId, Func<Build, bool> askable)
    {
        var selected = Selection(state, connectionId, askable);
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

    /// <summary>
    /// Every connection's pipelines before a deferral drops any, for what must hold whether or not
    /// a row is shown. A pipeline whose own failure was deferred and whose failed branches had all
    /// folded went from <see cref="Pipelines"/>, its folded branches with it, so the answers that
    /// folded them were dropped as no longer failing, and the rows came back until asked again.
    /// </summary>
    public static IEnumerable<PipelineBuilds> Undeferred(SessionState state)
    {
        var askable = BranchHosts.Askable(state.Connections.Select(_ => _.Connection));
        return state.Connections.SelectMany(_ => Selection(state, _.Connection.Id, askable));
    }

    static ImmutableArray<PipelineBuilds> Selection(SessionState state, string connectionId, Func<Build, bool> askable) =>
        BuildSelection.Select(
            Filters.Apply(state.Settings.Filters, state.Builds.Where(_ => _.ConnectionId == connectionId)),
            state.Settings.ShowOtherBranches,
            state.Verdicts,
            askable);
}
