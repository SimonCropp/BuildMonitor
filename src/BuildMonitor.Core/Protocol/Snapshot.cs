/// <summary>
/// The DTOs for one moment, from the state and the clock.
/// </summary>
static class Snapshot
{
    public static List<BuildDto> Builds(SessionState state, DateTimeOffset now) =>
        RowProjection.Rows(state)
            .SelectMany(_ => _.Builds.Select(build => Build(state, _.Connection.Connection, build, now)))
            .ToList();

    /// <summary>
    /// Searches the builds behind every row, not just the rows, so a build sharing a project's
    /// green row can still be read and retried by key.
    /// </summary>
    public static BuildDto? Find(SessionState state, string key, DateTimeOffset now)
    {
        foreach (var row in RowProjection.Rows(state))
        {
            foreach (var build in row.Builds)
            {
                if (build.Key == key)
                {
                    return Build(state, row.Connection.Connection, build, now);
                }
            }
        }

        return null;
    }

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
