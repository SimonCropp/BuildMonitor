/// <summary>
/// An account the size of the GitHub connection in the Debug head's log: 168 repositories, 588
/// workflows and five runs of each, 2,940 builds. Most latest runs passed, so a repository's
/// workflows fold into a closed group; one workflow in twelve failed and one in twenty five is
/// running. The canonical fixtures hold a handful of builds, which hid every cost that grows with
/// the pipelines.
/// </summary>
static class LargeAccount
{
    public const string ConnectionId = "github";

    public static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    static readonly Connection connection = new()
    {
        Id = ConnectionId,
        ProviderId = "github",
        Name = "GitHub",
        Auth = AuthMethod.Token
    };

    /// <summary>
    /// Polled, in a window showing 24 rows.
    /// </summary>
    public static SessionState State()
    {
        var state = MonitorSession.Resize(SessionState.Start(new() { Connections = [connection] }), 120, 30);
        return MonitorSession.ApplyPoll(state, ConnectionId, [], Builds(), Now);
    }

    public static ImmutableArray<Build> Builds()
    {
        var builds = ImmutableArray.CreateBuilder<Build>(2940);
        var workflow = 0;
        for (var repository = 0; repository < 168; repository++)
        {
            // Three or four workflows a repository, 588 in all.
            var workflows = repository % 2 == 0 ? 3 : 4;
            for (var index = 0; index < workflows; index++)
            {
                for (var run = 0; run < 5; run++)
                {
                    builds.Add(Build($"owner/repository{repository}", workflow, run));
                }

                workflow++;
            }
        }

        return builds.MoveToImmutable();
    }

    static Build Build(string repository, int workflow, int run)
    {
        var status = Status(workflow, run);
        var started = Now - TimeSpan.FromHours(workflow % 48 + run * 6) - TimeSpan.FromMinutes(5);
        var finished = status == BuildStatus.Running ? (DateTimeOffset?) null : started + TimeSpan.FromMinutes(4);
        return new(
            ConnectionId,
            $"{repository}/{workflow}",
            $"workflow{workflow}.yml",
            repository,
            "main",
            (1000 - run).ToString(),
            status,
            null,
            started,
            started,
            finished,
            null,
            $"https://github.com/{repository}/actions/runs/{workflow * 10 + run}",
            $"https://github.com/{repository}/tree/main",
            null,
            null,
            "0123456789abcdef",
            "Fix the thing",
            $"author{workflow % 40}",
            status != BuildStatus.Running,
            status == BuildStatus.Running,
            $"{workflow}/{run}");
    }

    static BuildStatus Status(int workflow, int run)
    {
        if (run > 0)
        {
            return BuildStatus.Succeeded;
        }

        if (workflow % 25 == 0)
        {
            return BuildStatus.Running;
        }

        if (workflow % 12 == 0)
        {
            return BuildStatus.Failed;
        }

        return BuildStatus.Succeeded;
    }
}
