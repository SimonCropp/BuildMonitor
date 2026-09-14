/// <summary>
/// https://api.gocd.org/current/
/// <para>
/// A GoCD pipeline is a sequence of stages; the row folds them into one status, and a retry
/// re-runs the failed jobs of the stage that failed, or schedules the pipeline again when
/// nothing did.
/// </para>
/// </summary>
sealed class GoCdProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.GoCd;

    static readonly KeyValuePair<string, string> confirm = new("X-GoCD-Confirm", "true");

    /// <summary>
    /// GoCD rejects a history page_size outside 10 to 100 with a 400, so asking for just the five
    /// builds a row needs failed every poll. It asks for at least this many and keeps the newest.
    /// </summary>
    const int minimumPageSize = 10;

    public override Uri BaseAddress(Connection connection) =>
        new(base.BaseAddress(connection), "go/api/");

    public override IEnumerable<KeyValuePair<string, string>> Headers =>
    [
        new("Accept", "application/vnd.go.cd+json")
    ];

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var dashboard = await Dashboard(context, cancel);
        var pipelines = new List<Pipeline>();
        var server = Server(context);
        foreach (var group in dashboard.PipelineGroups)
        {
            pipelines.AddRange(group.Pipelines.Select(_ => new Pipeline(_, _, group.Name, group.Name, $"{server}/go/pipeline/activity/{Encode(_)}")));
        }

        return pipelines;
    }

    /// <summary>
    /// Until its cache has loaded after a restart, GoCD answers the dashboard with a 202 and a
    /// message in place of pipelines. Read as an empty server, every row vanished for a poll;
    /// failing the poll instead keeps the rows until the dashboard is ready.
    /// </summary>
    static async Task<GoCdDashboardEmbedded> Dashboard(ProviderContext context, Cancel cancel)
    {
        var dashboard = await context.Http.Get("dashboard", GoCdContext.Default.GoCdDashboard, cancel);
        return dashboard.Embedded ?? throw new HttpRequestException("GoCD is still loading its dashboard");
    }

    /// <summary>
    /// The dashboard discovery reads, with each pipeline's instance counters and stage statuses
    /// as its token. Its ETag changes only when something visible does, so an unchanged dashboard is
    /// a 304, where fetching history for every pipeline returns a full response each time. Last
    /// updated times are not used: a configuration change bumps them on every pipeline. Progress
    /// inside a job does not change the dashboard, which the schedule covers.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        var dashboard = await Dashboard(context, cancel);
        var tokens = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var pipeline in dashboard.Pipelines)
        {
            var instances = (pipeline.Embedded?.Instances ?? [])
                .Select(_ => $"{_.Counter}:{string.Join(',', (_.Embedded?.Stages ?? []).Select(stage => $"{stage.Name}={stage.Status}"))}");
            tokens[pipeline.Name] = string.Join(';', instances);
        }

        return tokens.ToImmutable();
    }

    static string Server(ProviderContext context) =>
        context.Http.BaseAddress.GetLeftPart(UriPartial.Authority);

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        var server = Server(context);
        var pageSize = Math.Clamp(perPipeline, minimumPageSize, 100);
        foreach (var pipeline in pipelines)
        {
            var history = await context.Http.Get($"pipelines/{Encode(pipeline.Id)}/history?page_size={pageSize}", GoCdContext.Default.GoCdHistory, cancel);
            builds.AddRange(history.Pipelines.Take(perPipeline).Select(_ => Convert(context.Connection.Id, server, pipeline, _)));
        }

        return builds;
    }

    static Build Convert(string connectionId, string server, Pipeline pipeline, GoCdInstance instance)
    {
        var stages = instance.Stages;
        var scheduled = stages.Where(_ => _.Scheduled).ToList();
        BuildStatus status;
        string text;
        if (stages.Any(_ => _.Status == "Building"))
        {
            status = BuildStatus.Running;
            text = "building";
        }
        else if (stages.Any(_ => _.Result == "Failed"))
        {
            status = BuildStatus.Failed;
            text = "failed";
        }
        else if (stages.Any(_ => _.Result == "Cancelled"))
        {
            status = BuildStatus.Cancelled;
            text = "cancelled";
        }
        else if (scheduled.Count > 0 &&
                 scheduled.All(_ => _.Result == "Passed"))
        {
            status = BuildStatus.Succeeded;
            text = scheduled.Count == stages.Count ? "passed" : "passed, awaiting approval";
        }
        else
        {
            status = BuildStatus.Queued;
            text = "scheduled";
        }

        var failed = stages.FirstOrDefault(_ => _.Result == "Failed");
        var last = scheduled.LastOrDefault() ?? stages.LastOrDefault();
        var modification = instance.BuildCause?.MaterialRevisions
            .SelectMany(_ => _.Modifications)
            .FirstOrDefault();
        var branch = instance.BuildCause?.MaterialRevisions
            .Select(_ => Branch(_.Material?.Description))
            .FirstOrDefault(_ => _ is not null);
        var started = instance.ScheduledDate is { } date ? DateTimeOffset.FromUnixTimeMilliseconds(date) : (DateTimeOffset?) null;
        var jobDates = stages.SelectMany(_ => _.Jobs).Select(_ => _.ScheduledDate).Where(_ => _ is not null).ToList();
        DateTimeOffset? finished = status is BuildStatus.Running or BuildStatus.Queued || jobDates.Count == 0
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(jobDates.Max()!.Value);
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            branch,
            instance.Counter.ToString(),
            status,
            text,
            started,
            started,
            finished,
            null,
            $"{server}/go/pipelines/value_stream_map/{Encode(pipeline.Id)}/{instance.Counter}",
            null,
            null,
            null,
            modification?.Revision,
            modification?.Comment,
            modification?.UserName,
            CanRetry: status is not (BuildStatus.Running or BuildStatus.Queued),
            CanCancel: status == BuildStatus.Running,
            Join(pipeline.Id, instance.Counter.ToString(), failed?.Name, failed?.Counter, last?.Name, last?.Counter),
            pipeline.Url);
    }

    /// <summary>
    /// A git material describes itself as "URL: ..., Branch: main".
    /// </summary>
    static string? Branch(string? description)
    {
        if (description is null)
        {
            return null;
        }

        var index = description.IndexOf("Branch: ", StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var rest = description[(index + 8)..];
        var end = rest.IndexOf(',');
        return end < 0 ? rest.Trim() : rest[..end].Trim();
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        if (parts[2].Length > 0)
        {
            return context.Http.Send(HttpMethod.Post, $"stages/{Encode(parts[0])}/{parts[1]}/{Encode(parts[2])}/{parts[3]}/run-failed-jobs", null, cancel, [confirm]);
        }

        return context.Http.Send(HttpMethod.Post, $"pipelines/{Encode(parts[0])}/schedule", HttpJson.Json("{}"), cancel, [confirm]);
    }

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Send(HttpMethod.Post, $"stages/{Encode(parts[0])}/{parts[1]}/{Encode(parts[4])}/{parts[5]}/cancel", null, cancel, [confirm]);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("current_user", GoCdContext.Default.GoCdUser, cancel);
        return new(true, $"Signed in as {user.LoginName}");
    }
}
