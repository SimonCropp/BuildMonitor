/// <summary>
/// https://docs.gitlab.com/api/pipelines/
/// </summary>
sealed class GitLabProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.GitLab;

    /// <summary>
    /// Reporter, the lowest access level that can read pipelines. Membership alone includes
    /// projects where the user is only a Guest, whose pipelines answer 403, and one of those put
    /// the whole connection into sign in required.
    /// </summary>
    const string readsPipelines = "min_access_level=20";

    /// <summary>
    /// Projects in one GraphQL request. A query may hold 10,000 characters, and fifty global ids
    /// with the fields stay well inside that.
    /// </summary>
    const int chunkSize = 50;

    const string pipelineFields = "id iid status ref sha createdAt updatedAt startedAt finishedAt user{name}";

    public override Uri BaseAddress(Connection connection) =>
        new(base.BaseAddress(connection), "api/v4/");

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var group = context.Scope("group");
        var path = group.Length == 0
            ? $"projects?membership=true&{readsPipelines}&simple=true&archived=false&order_by=last_activity_at&per_page=100"
            : $"groups/{Encode(group)}/projects?include_subgroups=true&{readsPipelines}&simple=true&archived=false&order_by=last_activity_at&per_page=100";
        var projects = await context.Http.Get(path, GitLabContext.Default.ListGitLabProject, cancel);
        return projects
            .Select(_ => new Pipeline(_.Id.ToString(), _.PathWithNamespace, _.PathWithNamespace, null, $"{_.WebUrl}/-/pipelines"))
            .ToList();
    }

    /// <summary>
    /// How long a server whose GraphQL failed is fetched over REST before GraphQL is asked again.
    /// Asked every poll, a server without it paid a failed request before the REST ones each time.
    /// </summary>
    static readonly TimeSpan graphRetry = TimeSpan.FromHours(1);

    const string graphFailed = "gitlab.graph-failed";

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        // In id order rather than discovery's, most recently active first, so a rediscovery that
        // reorders the projects keeps each request's URL, and the ETag cached for it.
        foreach (var chunk in pipelines.OrderBy(_ => long.Parse(_.Id, CultureInfo.InvariantCulture)).Chunk(chunkSize))
        {
            var covered = await Graph(context, chunk, perPipeline, builds, cancel);
            // Concurrently, as the whole connection is one group, and a project at a time cost a
            // request per project in turn every ten to thirty seconds while anything ran.
            var perProject = await Concurrently.Map(
                chunk.Where(_ => !covered.Contains(_.Id)).ToList(),
                (pipeline, token) => Rest(context, pipeline, perPipeline, token),
                cancel);
            builds.AddRange(perProject.SelectMany(_ => _));
        }

        return builds;
    }

    /// <summary>
    /// The latest pipelines of up to fifty projects in one GraphQL request, where REST needs a
    /// request per project and another per running pipeline for its start time; it also names who
    /// started each. Returns the projects it covered. A project missing from the answer, because
    /// the server has no GraphQL, rejected a field, or answered as if anonymous and left private
    /// projects out, is fetched over REST instead, so no project is dropped without a word.
    /// </summary>
    static async Task<HashSet<string>> Graph(ProviderContext context, Pipeline[] chunk, int perPipeline, List<Build> builds, Cancel cancel)
    {
        if (context.Memory.TryGet<DateTimeOffset>(graphFailed, out var failed) &&
            DateTimeOffset.UtcNow - failed < graphRetry)
        {
            return [];
        }

        var byGlobalId = chunk.ToDictionary(_ => $"gid://gitlab/Project/{_.Id}");
        var ids = string.Join(',', byGlobalId.Keys.Select(_ => $"\"{_}\""));
        var query = string.Concat(
            "{projects(ids:[",
            ids,
            "],first:",
            chunk.Length.ToString(CultureInfo.InvariantCulture),
            "){nodes{id pipelines(first:",
            perPipeline.ToString(CultureInfo.InvariantCulture),
            context.Since is { } since ? $",updatedAfter:\"{Iso(since)}\"" : "",
            "){nodes{",
            pipelineFields,
            "}}}}}");
        GitLabGraphResponse response;
        try
        {
            response = await context.Http.Get($"../graphql?query={Uri.EscapeDataString(query)}", GitLabGraphContext.Default.GitLabGraphResponse, cancel);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is null or HttpStatusCode.NotFound)
        {
            Log.Warning(exception, "GitLab GraphQL is unavailable; fetching over REST for an hour");
            context.Memory.Set(graphFailed, DateTimeOffset.UtcNow);
            return [];
        }

        if (response.Errors is { Count: > 0 } errors)
        {
            Log.Warning("GitLab GraphQL answered with an error; fetching over REST for an hour: {Error}", errors[0].Message);
            context.Memory.Set(graphFailed, DateTimeOffset.UtcNow);
            return [];
        }

        var covered = new HashSet<string>();
        foreach (var project in response.Data?.Projects?.Nodes ?? [])
        {
            if (!byGlobalId.TryGetValue(project.Id, out var pipeline))
            {
                continue;
            }

            covered.Add(pipeline.Id);
            foreach (var node in project.Pipelines?.Nodes ?? [])
            {
                builds.Add(Convert(context.Connection.Id, pipeline, Run(node, pipeline), node.User?.Name));
            }
        }

        return covered;
    }

    /// <summary>
    /// Updated rather than created, the one date both the GraphQL field and the REST listing take on
    /// every supported GitLab, so a retry of an older pipeline still shows.
    /// </summary>
    static string Iso(DateTimeOffset since) =>
        since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    static async Task<List<Build>> Rest(ProviderContext context, Pipeline pipeline, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        var updated = context.Since is { } since ? $"&updated_after={Encode(Iso(since))}" : "";
        var runs = await context.Http.Get($"projects/{pipeline.Id}/pipelines?per_page={perPipeline}{updated}", GitLabContext.Default.ListGitLabPipeline, cancel);
        foreach (var run in runs)
        {
            // The listing carries no timings; only a live run is worth the second call. They go on a
            // copy, as a listing answered with a 304 hands back the runs parsed for an earlier poll.
            var timed = run;
            if (run.Status is "running" or "pending")
            {
                var detail = await context.Http.Get($"projects/{pipeline.Id}/pipelines/{run.Id}", GitLabContext.Default.GitLabPipeline, cancel);
                timed = run with
                {
                    StartedAt = detail.StartedAt,
                    FinishedAt = detail.FinishedAt
                };
            }

            builds.Add(Convert(context.Connection.Id, pipeline, timed, null));
        }

        return builds;
    }

    static GitLabPipeline Run(GitLabGraphPipeline node, Pipeline pipeline)
    {
        var id = long.Parse(node.Id[(node.Id.LastIndexOf('/') + 1)..], CultureInfo.InvariantCulture);
        var web = pipeline.Url[..pipeline.Url.LastIndexOf("/-/", StringComparison.Ordinal)];
        return new()
        {
            Id = id,
            Iid = long.Parse(node.Iid, CultureInfo.InvariantCulture),
            Status = node.Status.ToLowerInvariant(),
            Ref = node.Ref,
            Sha = node.Sha,
            WebUrl = $"{web}/-/pipelines/{id}",
            CreatedAt = node.CreatedAt,
            UpdatedAt = node.UpdatedAt,
            StartedAt = node.StartedAt,
            FinishedAt = node.FinishedAt
        };
    }

    static Build Convert(string connectionId, Pipeline pipeline, GitLabPipeline run, string? author)
    {
        var status = run.Status switch
        {
            "created" or "waiting_for_resource" or "preparing" or "pending" or "scheduled" or "waiting_for_callback" => BuildStatus.Queued,
            "running" or "canceling" => BuildStatus.Running,
            "success" => BuildStatus.Succeeded,
            "failed" => BuildStatus.Failed,
            "canceled" => BuildStatus.Cancelled,
            _ => BuildStatus.Unknown
        };
        var web = pipeline.Url[..pipeline.Url.LastIndexOf("/-/", StringComparison.Ordinal)];
        string? mergeRequest = null;
        var branch = run.Ref;
        if (run.Ref is not null &&
            run.Ref.StartsWith("refs/merge-requests/", StringComparison.Ordinal))
        {
            mergeRequest = run.Ref.Split('/')[2];
            branch = null;
        }

        var finished = status is BuildStatus.Succeeded or BuildStatus.Failed or BuildStatus.Cancelled ? run.FinishedAt ?? run.UpdatedAt : null;
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            branch,
            run.Iid.ToString(),
            status,
            run.Status,
            run.CreatedAt,
            run.StartedAt ?? run.CreatedAt,
            finished,
            null,
            run.WebUrl,
            branch is null ? null : $"{web}/-/tree/{branch}",
            mergeRequest,
            mergeRequest is null ? null : $"{web}/-/merge_requests/{mergeRequest}",
            run.Sha,
            run.Name,
            author,
            CanRetry: status is BuildStatus.Failed or BuildStatus.Cancelled,
            CanCancel: status is BuildStatus.Queued or BuildStatus.Running,
            Join(pipeline.Id, run.Id.ToString()),
            web);
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Send(HttpMethod.Post, $"projects/{parts[0]}/pipelines/{parts[1]}/retry", null, cancel);
    }

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Send(HttpMethod.Post, $"projects/{parts[0]}/pipelines/{parts[1]}/cancel", null, cancel);
    }

    /// <summary>
    /// The traces of the pipeline's failed jobs, oldest first. Jobs allowed to fail are left out:
    /// they did not fail the pipeline, and their traces would bury the one that did.
    /// </summary>
    public override async Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var jobs = await context.Http.Get($"projects/{parts[0]}/pipelines/{parts[1]}/jobs?scope[]=failed&per_page=100", GitLabContext.Default.ListGitLabJob, cancel);
        var logs = new List<(string Name, string Log)>();
        foreach (var job in jobs.Where(_ => !_.AllowFailure).OrderBy(_ => _.Id))
        {
            var name = job.Stage is null ? job.Name : $"{job.Stage} / {job.Name}";
            logs.Add((name, await context.Http.GetLog($"projects/{parts[0]}/jobs/{job.Id}/trace", cancel)));
        }

        return Sections(logs);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("user", GitLabContext.Default.GitLabUser, cancel);
        return new(true, $"Signed in as {user.Username}");
    }
}
