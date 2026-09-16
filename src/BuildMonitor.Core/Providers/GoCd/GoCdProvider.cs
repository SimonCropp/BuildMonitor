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

    // Each dashboard pipeline's rights, as of the last discovery. See Rights.
    const string rights = "gocd.rights";

    public override Uri BaseAddress(Connection connection) =>
        new(base.BaseAddress(connection), "go/api/");

    public override IEnumerable<KeyValuePair<string, string>> Headers =>
    [
        new("Accept", "application/vnd.go.cd+json")
    ];

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var dashboard = await Dashboard(context, cancel);
        context.Memory.Set(rights, Rights(dashboard));
        var pipelines = new List<Pipeline>();
        var server = Server(context);
        foreach (var group in dashboard.PipelineGroups)
        {
            pipelines.AddRange(group.Pipelines.Select(_ => new Pipeline(_, _, group.Name, group.Name, $"{server}/go/pipeline/activity/{Encode(_)}")));
        }

        return pipelines;
    }

    /// <summary>
    /// Whether the user may operate each pipeline's group and its first stage. Every change the
    /// API makes needs the group, which the dashboard gives as can_pause, and each needs a stage
    /// too: scheduling the pipeline needs its first, the dashboard's can_operate, while re-running
    /// a stage's failed jobs or cancelling it needs that stage, which the history gives. A pipeline
    /// the dashboard leaves out, or a flag an older GoCD does not send, takes nothing away.
    /// </summary>
    static ImmutableDictionary<string, (bool Group, bool FirstStage)> Rights(GoCdDashboardEmbedded dashboard)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, (bool Group, bool FirstStage)>(StringComparer.OrdinalIgnoreCase);
        foreach (var pipeline in dashboard.Pipelines)
        {
            builder[pipeline.Name] = (pipeline.CanPause != false, pipeline.CanOperate != false);
        }

        return builder.ToImmutable();
    }

    static (bool Group, bool FirstStage) Rights(ProviderContext context, Pipeline pipeline)
    {
        if (context.Memory.TryGet<ImmutableDictionary<string, (bool Group, bool FirstStage)>>(rights, out var known) &&
            known.TryGetValue(pipeline.Id, out var pipelineRights))
        {
            return pipelineRights;
        }

        return (true, true);
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
            var pipelineRights = Rights(context, pipeline);
            builds.AddRange(history.Pipelines.Take(perPipeline).Select(_ => Convert(context.Connection.Id, server, pipeline, _, pipelineRights)));
        }

        return builds;
    }

    static Build Convert(string connectionId, string server, Pipeline pipeline, GoCdInstance instance, (bool Group, bool FirstStage) rights)
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
        // A retry re-runs the failed jobs of the stage that failed, which needs that stage, or
        // schedules the pipeline when none did, which needs its first. A cancel stops the last
        // stage scheduled, which needs that one.
        var retryStage = failed is null ? rights.FirstStage : failed.OperatePermission != false;
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
            CanRetry: rights.Group &&
                      retryStage &&
                      status is not (BuildStatus.Running or BuildStatus.Queued),
            CanCancel: rights.Group &&
                       last?.OperatePermission != false &&
                       status == BuildStatus.Running,
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

    /// <summary>
    /// The consoles of the failed jobs of the failed stages. A console is an artifact file rather
    /// than an API resource, and GoCD answers a request whose Accept header it does not serve with a
    /// 404, so the console request accepts anything rather than the API's JSON.
    /// </summary>
    public override async Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var pipeline = Encode(parts[0]);
        var instance = await context.Http.Get($"pipelines/{pipeline}/{parts[1]}", GoCdContext.Default.GoCdInstance, cancel);
        var logs = new List<(string Name, string Log)>();
        foreach (var stage in instance.Stages.Where(_ => _.Result == "Failed"))
        {
            foreach (var job in stage.Jobs.Where(_ => _.Result == "Failed"))
            {
                var path = $"../files/{pipeline}/{parts[1]}/{Encode(stage.Name)}/{stage.Counter}/{Encode(job.Name)}/cruise-output/console.log";
                logs.Add(($"{stage.Name} / {job.Name}", await context.Http.GetLog(path, cancel, "*/*")));
            }
        }

        return Sections(logs);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("current_user", GoCdContext.Default.GoCdUser, cancel);
        return new(true, $"Signed in as {user.LoginName}");
    }
}
