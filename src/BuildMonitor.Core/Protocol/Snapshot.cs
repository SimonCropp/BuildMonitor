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

    public static BuildDto? Find(SessionState state, string key, DateTimeOffset now) =>
        RowProjection.Builds(state).FirstOrDefault(_ => _.Key == key) is { } build
            ? Build(state, state.Connection(build.ConnectionId)!.Connection, build, now)
            : null;

    static BuildDto Build(SessionState state, Connection connection, Build build, DateTimeOffset now)
    {
        var (fraction, timing) = Progress.Compute(build, Estimator.Estimate(build, state.Medians), now);
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
            build.CanRetry,
            build.CanCancel);
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
