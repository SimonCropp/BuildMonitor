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
    /// Developer, the lowest access level that can retry and cancel pipelines.
    /// </summary>
    const string changesPipelines = "min_access_level=30";

    const int pageSize = 100;

    // Whether the user is an administrator, whose rights do not come from a role in each project,
    // and the projects the user may only watch, as of the last discovery.
    const string administrator = "gitlab.administrator";
    const string readOnly = "gitlab.read-only";

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
        var projects = await context.Http.Get(Listing(context, readsPipelines), GitLabContext.Default.ListGitLabProject, cancel);
        context.Memory.Set(readOnly, await ReadOnly(context, projects, cancel));
        return projects
            .Select(_ => new Pipeline(_.Id.ToString(), _.PathWithNamespace, _.PathWithNamespace, null, $"{_.WebUrl}/-/pipelines", _.WebUrl))
            .ToList();
    }

    static string Listing(ProviderContext context, string accessLevel)
    {
        var group = context.Scope("group");
        if (group.Length == 0)
        {
            return $"projects?membership=true&{accessLevel}&simple=true&archived=false&order_by=last_activity_at&per_page={pageSize}";
        }

        return $"groups/{Encode(group)}/projects?include_subgroups=true&{accessLevel}&simple=true&archived=false&order_by=last_activity_at&per_page={pageSize}";
    }

    /// <summary>
    /// The projects where the user is only a Reporter, which answer a retry or a cancel with 403:
    /// those missing from the same listing at Developer. A full page of that listing can leave out a
    /// project the first listing has, when activity reorders the two between requests, so then none
    /// are read only. Not asked when the connection can only watch, which offers nothing anyway, or
    /// for an administrator. A listing that fails keeps what the last one found.
    /// </summary>
    static async Task<ImmutableHashSet<string>> ReadOnly(ProviderContext context, List<GitLabProject> projects, Cancel cancel)
    {
        if (context.Access == BuildAccess.Watch ||
            (context.Memory.TryGet<bool>(administrator, out var isAdministrator) && isAdministrator))
        {
            return [];
        }

        List<GitLabProject> changeable;
        try
        {
            changeable = await context.Http.Get(Listing(context, changesPipelines), GitLabContext.Default.ListGitLabProject, cancel);
        }
        catch (HttpRequestException exception)
        {
            Log.Warning(exception, "Listing the GitLab projects {Connection} may change failed", context.Connection.Name);
            return ReadOnly(context);
        }

        if (changeable.Count >= pageSize)
        {
            return [];
        }

        var ids = changeable.Select(_ => _.Id).ToHashSet();
        return [..projects.Where(_ => !ids.Contains(_.Id)).Select(_ => _.Id.ToString())];
    }

    static ImmutableHashSet<string> ReadOnly(ProviderContext context)
    {
        if (context.Memory.TryGet<ImmutableHashSet<string>>(readOnly, out var known))
        {
            return known;
        }

        return [];
    }

    /// <summary>
    /// How long a server whose GraphQL failed is fetched over REST before GraphQL is asked again.
    /// Asked every poll, a server without it paid a failed request before the REST ones each time.
    /// </summary>
    static TimeSpan graphRetry = TimeSpan.FromHours(1);

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

        var readOnlyProjects = ReadOnly(context);
        return [..builds.Select(_ => Offered(_, readOnlyProjects))];
    }

    static Build Offered(Build build, ImmutableHashSet<string> readOnlyProjects)
    {
        if (readOnlyProjects.Contains(build.PipelineId))
        {
            return build.WatchOnly();
        }

        return build;
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
        var web = pipeline.RepoUrl!;
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
        var web = pipeline.RepoUrl!;
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
            pipeline.Url,
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

    /// <summary>
    /// The archives of the same failed jobs the log comes from. The job listing already carries
    /// each archive's name and size, so listing costs one request and no guesswork about sizes.
    /// <para>
    /// Only the archive is offered. A job's other artifact types, its junit report and its
    /// metadata, are reachable only by a path inside the archive, which the listing does not give,
    /// and the archive holds them anyway.
    /// </para>
    /// </summary>
    public override async Task<IReadOnlyList<BuildArtifact>> ListArtifacts(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var jobs = await context.Http.Get($"projects/{parts[0]}/pipelines/{parts[1]}/jobs?scope[]=failed&per_page=100", GitLabContext.Default.ListGitLabJob, cancel);
        var now = DateTimeOffset.UtcNow;
        return jobs
            .Where(_ => !_.AllowFailure &&
                        _.ArtifactsFile is { Filename.Length: > 0 })
            .OrderBy(_ => _.Id)
            .Select(_ => new BuildArtifact(
                _.Id.ToString(),
                // Named after the job, because every job calls its archive the same thing and a
                // list of four artifacts.zip says nothing about which stage broke.
                $"{_.Name}-{_.ArtifactsFile!.Filename}",
                _.ArtifactsFile.Size,
                _.ArtifactsExpireAt <= now ? "expired" : null))
            .ToList();
    }

    public override Task<long> DownloadArtifact(ProviderContext context, Build build, BuildArtifact artifact, Stream destination, long maxBytes, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Download($"projects/{parts[0]}/jobs/{artifact.Id}/artifacts", destination, maxBytes, cancel);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("user", GitLabContext.Default.GitLabUser, cancel);
        return new(true, $"Signed in as {user.Username}", await AccessOrUnknown(context, cancel));
    }

    /// <summary>
    /// The token's scopes: api may retry and cancel, read_api only watch. A personal, project or
    /// group access token reads its own from personal_access_tokens/self, which refuses an OAuth
    /// token, and a sign in's token reads them from oauth/token/info instead. A granular token holds
    /// its rights outside its scopes, so it is left unknown. Whether the user is an administrator is
    /// kept for discovery, which otherwise narrows by the user's role in each project. Admin Mode can
    /// still deny an administrator's token what the role would not, which then only offers what a
    /// refusal explains.
    /// </summary>
    public override async Task<BuildAccess> Access(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("user", GitLabContext.Default.GitLabUser, cancel);
        context.Memory.Set(administrator, user.IsAdmin);
        if (context.Connection.Auth != AuthMethod.Token)
        {
            // Beside the API, not in it, so a server under a path keeps its path.
            var info = await context.Http.Get("../../oauth/token/info", GitLabContext.Default.GitLabTokenInfo, cancel);
            return ByScopes(info.Scope);
        }

        GitLabToken token;
        try
        {
            token = await context.Http.Get("personal_access_tokens/self", GitLabContext.Default.GitLabToken, cancel);
        }
        // An older GitLab has no such route.
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return BuildAccess.Unknown;
        }

        if (token.Granular)
        {
            return BuildAccess.Unknown;
        }

        return ByScopes(token.Scopes);
    }

    static BuildAccess ByScopes(List<string> scopes)
    {
        if (scopes.Contains("api"))
        {
            return BuildAccess.Change;
        }

        if (scopes.Contains("read_api"))
        {
            return BuildAccess.Watch;
        }

        return BuildAccess.Unknown;
    }
}
