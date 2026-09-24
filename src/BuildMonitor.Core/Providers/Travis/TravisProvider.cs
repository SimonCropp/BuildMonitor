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
            .Select(_ => new Pipeline(_.Slug, _.Slug, _.Slug, null, $"{Web(context, _.VcsType)}/{_.Slug}", RepoUrl(context, _.Slug, _.VcsType), DefaultBranch: _.DefaultBranch?.Name))
            .ToList();
    }

    static bool Hosted(ProviderContext context) =>
        context.Http.BaseAddress.Host == "api.travis-ci.com";

    /// <summary>
    /// The web app for the API in use: app.travis-ci.com for the hosted service, the server
    /// itself for Enterprise, under the source the repository is on, as the app files it.
    /// </summary>
    static string Web(ProviderContext context, string? vcsType)
    {
        var source = vcsType switch
        {
            "BitbucketRepository" => "bitbucket",
            "GitlabRepository" => "gitlab",
            "AssemblaRepository" => "assembla",
            _ => "github"
        };
        if (Hosted(context))
        {
            return $"https://app.travis-ci.com/{source}";
        }

        return $"{context.Http.BaseAddress.Scheme}://{context.Http.BaseAddress.Host}/{source}";
    }

    /// <summary>
    /// The repository's page for a build whose commit names none, as <see cref="RepositoryPage"/>
    /// reads it: on the hosted service, the host of the source the listing says it is on, since a
    /// Bitbucket or GitLab repository composed on github.com opened another repository of the
    /// name, or none. An Assembla repository's page is under an id Travis does not list, and an
    /// Enterprise server may build from a GitHub Enterprise server of any name, which the listing
    /// does not give, so neither has one.
    /// </summary>
    static string? RepoUrl(ProviderContext context, string slug, string? vcsType)
    {
        if (!Hosted(context))
        {
            return null;
        }

        return vcsType switch
        {
            "BitbucketRepository" => $"https://bitbucket.org/{slug}",
            "GitlabRepository" => $"https://gitlab.com/{slug}",
            "AssemblaRepository" => null,
            _ => $"https://github.com/{slug}"
        };
    }

    /// <summary>
    /// The repository's page on the host its commit's compare_url is on, the one address Travis
    /// gives on the repository's host: GitHub Enterprise's for a Travis CI Enterprise server built
    /// from one. Composed on github.com, such a build's repository, branch and pull request links
    /// opened repositories github.com does not have. None on Assembla, whose pages are not under
    /// the repository's name.
    /// </summary>
    static string? RepositoryPage(Pipeline pipeline, TravisBuild build)
    {
        if (build.Commit?.CompareUrl is { } compare &&
            Uri.TryCreate(compare, UriKind.Absolute, out var address))
        {
            if (address.Host.Contains("assembla", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return $"{address.GetLeftPart(UriPartial.Authority)}/{pipeline.RepoName}";
        }

        return pipeline.RepoUrl;
    }

    /// <summary>
    /// A branch's page and a pull request's under <paramref name="web"/>, in the form of the service
    /// it is on, read from its host as the row's mark is: a Bitbucket or GitLab repository's
    /// composed as GitHub's opened pages neither has.
    /// </summary>
    static (string Branch, string PullRequest) Paths(string web) =>
        RepoHosts.MarkOf(web) switch
        {
            "host-bitbucket" => ("branch", "pull-requests"),
            "host-gitlab" => ("-/tree", "-/merge_requests"),
            _ => ("tree", "pull")
        };

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        foreach (var pipeline in pipelines)
        {
            // A repository deleted since discovery answers with a 404, which would fail the whole
            // fetch; it has no builds until the next discovery drops it.
            var response = await GetOrNone(
                context,
                $"repo/{Encode(pipeline.Id)}/builds?limit={perPipeline}&sort_by=id:desc&include=build.commit",
                TravisContext.Default.TravisBuilds,
                cancel);
            if (response is null)
            {
                continue;
            }

            var fetched = response.Builds.Select(_ => Convert(context, pipeline, _)).ToList();
            builds.AddRange(fetched);
            if (pipeline.DefaultBranch is { } branch &&
                fetched.All(_ => _.Branch != branch) &&
                await DefaultRun(context, pipeline, branch, cancel) is { } own)
            {
                builds.Add(own);
            }
        }

        return builds;
    }

    /// <summary>
    /// The repository's newest build on its default branch, where its last few left it out, as a
    /// burst of pull requests does. By branch and by every event but a pull request's, since a pull
    /// request build's branch is the one it targets and the branch filter alone keeps them. A
    /// repository with none since the history cutoff is not asked again for an hour.
    /// </summary>
    static async Task<Build?> DefaultRun(ProviderContext context, Pipeline pipeline, string branch, Cancel cancel)
    {
        var memory = context.Memory;
        var now = DateTimeOffset.UtcNow;
        if (DefaultRunMemory.KnownNone(memory, pipeline.Id, branch, now))
        {
            return null;
        }

        var response = await GetOrNone(
            context,
            $"repo/{Encode(pipeline.Id)}/builds?limit=1&sort_by=id:desc&branch.name={Encode(branch)}&event_type=push,api,cron&include=build.commit",
            TravisContext.Default.TravisBuilds,
            cancel);
        if (response?.Builds.FirstOrDefault() is not { } run)
        {
            DefaultRunMemory.None(memory, pipeline.Id, branch, now);
            return null;
        }

        var build = Convert(context, pipeline, run);
        if (build.Branch != branch ||
            (context.Since is { } since && !HistoryCutoff.Keeps(build, since)))
        {
            DefaultRunMemory.None(memory, pipeline.Id, branch, now);
            return null;
        }

        DefaultRunMemory.Found(memory, pipeline.Id, branch);
        return build;
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
        var pullRequest = build.PullRequestNumber?.ToString();
        var web = RepositoryPage(pipeline, build);
        var branch = build.Branch?.Name;
        string? branchUrl = null;
        string? pullRequestUrl = null;
        if (web is not null)
        {
            var paths = Paths(web);
            if (branch is not null)
            {
                branchUrl = $"{web}/{paths.Branch}/{branch}";
            }

            if (pullRequest is not null)
            {
                pullRequestUrl = $"{web}/{paths.PullRequest}/{pullRequest}";
            }
        }

        // A pull request build's branch is the one it targets, which filed every pull request as a
        // build of main, and Travis names the branch it came from nowhere: the pull request's own
        // ref is what it has.
        if (pullRequest is not null)
        {
            branch = PullRequestBranches.Unnamed(pullRequest);
            branchUrl = null;
        }

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
            branchUrl,
            pullRequest,
            pullRequestUrl,
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
            pipeline.Url,
            web,
            DefaultBranch: pipeline.DefaultBranch);
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
