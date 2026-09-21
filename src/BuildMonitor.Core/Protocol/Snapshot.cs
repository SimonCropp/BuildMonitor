/// <summary>
/// The DTOs for one moment, from the state and the clock.
/// </summary>
static class Snapshot
{
    /// <summary>
    /// The builds, not the rows: an open group lists its members twice, once behind its own row and
    /// once as member rows, and a closed one hides them.
    /// </summary>
    public static List<BuildDto> Builds(SessionState state, DateTimeOffset now) =>
        RowProjection.Builds(state)
            .Select(_ => Build(state, state.Connection(_.ConnectionId)!.Connection, _, now))
            .ToList();

    public static BuildDto? Find(SessionState state, string key, DateTimeOffset now)
    {
        if (RowProjection.Builds(state).FirstOrDefault(_ => _.HasKey(key)) is { } build)
        {
            return Build(state, state.Connection(build.ConnectionId)!.Connection, build, now);
        }

        return null;
    }

    /// <summary>
    /// Every run of one pipeline the tray holds, newest first, which is what the rows collapse to
    /// the latest. Whether a failure is the first or the fourth in a row is the next thing a
    /// reader wants, and the poll fetched the runs that answer it already.
    /// <para>
    /// A build key and a pipeline key both resolve: the keys the other verbs hand out are build
    /// keys, and which branch a run was on is not what a history is about. Null when neither
    /// names a pipeline. Runs on one branch share a key and differ by run number.
    /// </para>
    /// </summary>
    public static List<BuildDto>? Runs(SessionState state, string key, DateTimeOffset now)
    {
        var builds = Filters.Apply(state.Settings.Filters, state.Builds);
        if (RunOf(builds, key) is not { } run)
        {
            return null;
        }

        return builds
            .Where(_ => _.SamePipeline(run))
            // A stable sort, so runs that started at the same moment keep the order the provider
            // listed them in, which is newest first.
            .OrderByDescending(_ => _.Ordering ?? DateTimeOffset.MinValue)
            .Select(_ => Build(state, state.Connection(_.ConnectionId)!.Connection, _, now))
            .ToList();
    }

    /// <summary>
    /// A run of the pipeline the key names, as a build key or as a pipeline key.
    /// </summary>
    static Build? RunOf(ImmutableArray<Build> builds, string key) =>
        builds.FirstOrDefault(_ => _.HasKey(key)) ??
        builds.FirstOrDefault(_ => _.HasPipelineKey(key));

    public static List<PipelineDto> Pipelines(SessionState state)
    {
        var builds = Filters.Apply(state.Settings.Filters, state.Builds);
        return state.Connections
            .SelectMany(_ => _.Pipelines
                .Where(pipeline => !Filters.ExcludesPipeline(state.Settings.Filters, pipeline))
                .Select(pipeline => Pipeline(builds, _.Connection, pipeline, state.LocalRepos)))
            .ToList();
    }

    static PipelineDto Pipeline(ImmutableArray<Build> builds, Connection connection, Pipeline pipeline, ImmutableDictionary<string, string> localRepos) =>
        new(
            $"{connection.Id}/{pipeline.Id}",
            connection.Name,
            pipeline.Name,
            pipeline.RepoName,
            pipeline.Group,
            pipeline.Url,
            builds.Count(_ => _.ConnectionId == connection.Id &&
                              _.PipelineId == pipeline.Id),
            LocalRepos.Find(localRepos, pipeline.RepoName));

    static BuildDto Build(SessionState state, Connection connection, Build build, DateTimeOffset now)
    {
        var estimate = Estimator.Estimate(build, state.Medians);
        var (fraction, timing) = Progress.Compute(build, estimate, now);
        return new(
            build.Key,
            connection.Name,
            build.PipelineName,
            build.RepoName,
            build.Branch,
            build.RunNumber,
            build.Status.ToString(),
            build.StatusText,
            build.Started,
            build.Finished,
            fraction,
            timing,
            build.BuildUrl,
            build.BranchUrl,
            build.PullRequestNumber,
            build.PullRequestUrl,
            build.CommitSha,
            build.CommitMessage,
            build.Author,
            build.Retryable(),
            build.CanCancel,
            build.CanRunNext(ProviderDescriptors.Get(connection.ProviderId)),
            LocalRepos.Find(state.LocalRepos, build));
    }

    public static List<ConnectionDto> Connections(SessionState state) =>
        state.Connections
            .Select(_ => new ConnectionDto(
                _.Connection.Id,
                _.Connection.Name,
                ProviderDescriptors.Get(_.Connection.ProviderId).Name,
                _.Health.ToString(),
                _.Error,
                _.LastPolled,
                _.Pipelines.Length))
            .ToList();

    public static SummaryDto Summary(SessionState state, DateTimeOffset now)
    {
        var builds = Builds(state, now);
        var screen = ScreenBuilder.Build(state, now);
        return new(
            state.Connections.Length,
            builds.Count,
            builds.Count(_ => _.Status == nameof(BuildStatus.Failed)),
            builds.Count(_ => _.Status is nameof(BuildStatus.Running) or nameof(BuildStatus.Queued)),
            screen.Tray.Icon.ToString(),
            screen.Status,
            Connections(state));
    }
}
