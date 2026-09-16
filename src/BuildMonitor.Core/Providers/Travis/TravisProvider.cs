/// <summary>
/// https://developer.travis-ci.com/resource/builds
/// </summary>
sealed class TravisProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.Travis;

    public override IEnumerable<KeyValuePair<string, string>> Headers =>
    [
        new("Travis-API-Version", "3")
    ];

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var repositories = await context.Http.Get("repos?repository.active=true&limit=100&sort_by=default_branch.last_build:desc", TravisContext.Default.TravisRepositories, cancel);
        return repositories.Repositories
            .Select(_ => new Pipeline(_.Slug, _.Slug, _.Slug, null, $"{Web(context)}/{_.Slug}"))
            .ToList();
    }

    /// <summary>
    /// The web app for the API in use: app.travis-ci.com for the hosted service, the server
    /// itself for Enterprise.
    /// </summary>
    static string Web(ProviderContext context)
    {
        var host = context.Http.BaseAddress.Host;
        if (host == "api.travis-ci.com")
        {
            return "https://app.travis-ci.com/github";
        }

        return $"{context.Http.BaseAddress.Scheme}://{host}/github";
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        foreach (var pipeline in pipelines)
        {
            var response = await context.Http.Get(
                $"repo/{Encode(pipeline.Id)}/builds?limit={perPipeline}&sort_by=id:desc&include=build.commit",
                TravisContext.Default.TravisBuilds,
                cancel);
            builds.AddRange(response.Builds.Select(_ => Convert(context, pipeline, _)));
        }

        return builds;
    }

    /// <summary>
    /// The repositories with the newest builds first, each with its last started build's id and
    /// state as its token, which a new build or a finished one moves. Travis sends no ETags, so
    /// without this every quiet repository cost a full response each time its schedule came round.
    /// A build created but not yet started is not in the listing, and moves the token once it starts.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        var repositories = await context.Http.Get(
            "repos?repository.active=true&limit=100&sort_by=current_build:desc&include=repository.last_started_build",
            TravisContext.Default.TravisRepositories,
            cancel);
        return repositories.Repositories
            .Where(_ => _.LastStartedBuild is not null)
            .ToImmutableDictionary(_ => _.Slug, _ => $"{_.LastStartedBuild!.Id}|{_.LastStartedBuild.State}");
    }

    static Build Convert(ProviderContext context, Pipeline pipeline, TravisBuild build)
    {
        var status = build.State switch
        {
            "created" or "queued" or "received" => BuildStatus.Queued,
            "started" => BuildStatus.Running,
            "passed" => BuildStatus.Succeeded,
            "failed" or "errored" => BuildStatus.Failed,
            "canceled" => BuildStatus.Cancelled,
            _ => BuildStatus.Unknown
        };
        var branch = build.Branch?.Name;
        var pullRequest = build.PullRequestNumber?.ToString();
        return new(
            context.Connection.Id,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            branch,
            build.Number,
            status,
            build.State,
            null,
            build.StartedAt,
            build.FinishedAt,
            null,
            $"{pipeline.Url}/builds/{build.Id}",
            branch is null ? null : $"https://github.com/{pipeline.RepoName}/tree/{branch}",
            pullRequest,
            pullRequest is null ? null : $"https://github.com/{pipeline.RepoName}/pull/{pullRequest}",
            build.Commit?.Sha,
            build.Commit?.Message,
            build.Commit?.Author?.Name,
            // The build's permissions are the checks a restart or a cancel meets, so a false one
            // would only be refused.
            CanRetry: build.Permissions?.Restart != false &&
                      status is BuildStatus.Succeeded or BuildStatus.Failed or BuildStatus.Cancelled,
            CanCancel: build.Permissions?.Cancel != false &&
                       status is BuildStatus.Queued or BuildStatus.Running,
            build.Id.ToString(),
            $"https://github.com/{pipeline.RepoName}");
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel) =>
        context.Http.Send(HttpMethod.Post, $"build/{build.ProviderRef}/restart", null, cancel);

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel) =>
        context.Http.Send(HttpMethod.Post, $"build/{build.ProviderRef}/cancel", null, cancel);

    /// <summary>
    /// The logs of the build's failed and errored jobs, leaving out jobs allowed to fail, which did
    /// not fail the build. Read as log.txt, which is plain text whatever the request accepts and
    /// holds an archived log as well as a recent one.
    /// </summary>
    public override async Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel)
    {
        var jobs = await context.Http.Get($"build/{build.ProviderRef}/jobs", TravisContext.Default.TravisJobs, cancel);
        var logs = new List<(string Name, string Log)>();
        foreach (var job in jobs.Jobs.Where(_ => _ is { State: "failed" or "errored", AllowFailure: false }))
        {
            logs.Add(($"Job {job.Number}", await context.Http.GetLog($"job/{job.Id}/log.txt", cancel)));
        }

        return Sections(logs);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("user", TravisContext.Default.TravisUser, cancel);
        return new(true, $"Signed in as {user.Login}");
    }
}
