/// <summary>
/// https://docs.github.com/en/rest/actions/workflow-runs
/// <para>
/// Runs are fetched per repository, not per workflow: one call covers every workflow in it, and
/// with the ETag cache an unchanged repository answers 304, which GitHub does not count against
/// the rate limit.
/// </para>
/// </summary>
sealed class GitHubProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.GitHub;

    /// <summary>
    /// Repositories with no push this long are not discovered. Discovery costs a call per
    /// repository, and an account can have hundreds of dormant forks.
    /// </summary>
    static readonly TimeSpan activeWindow = TimeSpan.FromDays(90);

    const int maxPages = 5;

    public override IEnumerable<KeyValuePair<string, string>> Headers =>
    [
        new("Accept", "application/vnd.github+json"),
        new("X-GitHub-Api-Version", "2022-11-28")
    ];

    public override Uri BaseAddress(Connection connection)
    {
        var address = base.BaseAddress(connection);
        // GitHub Enterprise Server serves the API under /api/v3 of the host.
        if (address.Host != "api.github.com" &&
            address.AbsolutePath == "/")
        {
            return new(address, "api/v3/");
        }

        return address;
    }

    /// <summary>
    /// How often every active repository's workflows are listed, however long since it was pushed
    /// to: enabling or disabling a workflow probably does not move pushed_at.
    /// </summary>
    static readonly TimeSpan listEverything = TimeSpan.FromHours(1);

    // Each repository's workflows as last listed, with its pushed_at then, and when every active
    // repository's were last listed.
    const string listedWorkflows = "github.workflows";
    const string listedEverything = "github.listed-everything";
    // The active repositories the user may only read, as of the last discovery.
    const string readOnly = "github.read-only";

    /// <summary>
    /// The workflows of every repository pushed to lately. A repository's are listed again only
    /// once it has been pushed to since, and every repository's each hour: listing all of them each
    /// discovery cost a request a repository, which the secondary limit counts even as a 304, so 168
    /// repositories spent over a third of a minute's quota at once and deferred the groups due.
    /// </summary>
    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var started = Stopwatch.GetTimestamp();
        var repositories = await Repositories(context, cancel);
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - activeWindow;
        var active = repositories
            .Where(_ => _ is {Archived: false, Disabled: false} &&
                        _.PushedAt > cutoff &&
                        (context.ShowForksAndCollaborations || !_.Fork))
            .ToList();
        context.Memory.Set(readOnly, ReadOnly(context, active));
        var everything = !context.Memory.TryGet<DateTimeOffset>(listedEverything, out var listedAt) ||
                         now - listedAt >= listEverything;
        var known = ImmutableDictionary<string, (string Pushed, List<Pipeline> Pipelines)>.Empty;
        if (!everything &&
            context.Memory.TryGet<ImmutableDictionary<string, (string Pushed, List<Pipeline> Pipelines)>>(listedWorkflows, out var remembered))
        {
            known = remembered;
        }

        var stale = active
            .Where(_ => !known.TryGetValue(_.FullName, out var entry) || entry.Pushed != Pushed(_))
            .ToList();
        var perRepository = await Concurrently.Map(
            stale,
            async (repository, token) =>
            {
                var workflows = await context.Http.Get(
                    $"repos/{repository.FullName}/actions/workflows?per_page=100",
                    GitHubContext.Default.GitHubWorkflows,
                    token);
                // GitHub-managed workflows (Copilot code review and coding agent, Dependabot, Pages)
                // live under dynamic/. They stay active after a single run, so a PR reviewed months
                // ago would sit on the screen as a red row the repository never defined.
                return workflows.Workflows
                    .Where(_ => _.State == "active" &&
                                !_.Path.StartsWith("dynamic/", StringComparison.Ordinal))
                    .Select(_ => new Pipeline(
                        $"{repository.FullName}/{_.Id}",
                        _.Name,
                        repository.FullName,
                        repository.FullName,
                        $"{repository.HtmlUrl}/actions/workflows/{Path.GetFileName(_.Path)}"))
                    .ToList();
            },
            cancel,
            context.Progress);
        var next = ImmutableDictionary.CreateBuilder<string, (string Pushed, List<Pipeline> Pipelines)>();
        for (var index = 0; index < stale.Count; index++)
        {
            next[stale[index].FullName] = (Pushed(stale[index]), perRepository[index]);
        }

        // In the listing's order, most recently pushed first, whether listed now or remembered.
        var pipelines = new List<Pipeline>();
        foreach (var repository in active)
        {
            if (!next.TryGetValue(repository.FullName, out var listed))
            {
                listed = known[repository.FullName];
                next[repository.FullName] = listed;
            }

            pipelines.AddRange(listed.Pipelines);
        }

        context.Memory.Set(listedWorkflows, next.ToImmutable());
        if (everything)
        {
            context.Memory.Set(listedEverything, now);
        }

        Log.Information(
            "GitHub discovery: {Repositories} repositories, {Active} pushed in the last {Days} days, {Listed} of those listed, {Pipelines} workflows, {Elapsed:0.0}s",
            repositories.Count,
            active.Count,
            activeWindow.TotalDays,
            stale.Count,
            pipelines.Count,
            Stopwatch.GetElapsedTime(started).TotalSeconds);
        return pipelines;
    }

    static string Pushed(GitHubRepository repository) =>
        $"{repository.PushedAt:O}";

    /// <summary>
    /// The repositories whose runs the user may not re-run or cancel, which needs write access.
    /// Only for a token that lists its scopes, whose permissions are its user's. GitHub does not say
    /// whose a fine grained token's are, and its own push can be false where its Actions write lets
    /// it re-run, so theirs decide nothing.
    /// </summary>
    static ImmutableHashSet<string> ReadOnly(ProviderContext context, List<GitHubRepository> repositories)
    {
        if (context.Access != BuildAccess.Change)
        {
            return [];
        }

        return [..repositories.Where(_ => _.Permissions is { Push: false }).Select(_ => _.FullName)];
    }

    /// <summary>
    /// The repository listings discovery reads, most recently pushed first: the repositories the
    /// user owns or reaches through an organization, plus those they only collaborate on when
    /// asked for, or with an owner named, an organization, or a user when no organization of
    /// that name is found.
    /// </summary>
    static string[] Listings(ProviderContext context)
    {
        var owner = context.Scope("owner");
        if (owner.Length > 0)
        {
            return [$"orgs/{Encode(owner)}/repos?per_page=100&sort=pushed&type=all", $"users/{Encode(owner)}/repos?per_page=100&sort=pushed"];
        }

        if (context.ShowForksAndCollaborations)
        {
            return ["user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member"];
        }

        return ["user/repos?per_page=100&sort=pushed&affiliation=owner,organization_member"];
    }

    static async Task<List<GitHubRepository>> Repositories(ProviderContext context, Cancel cancel)
    {
        var listings = Listings(context);
        try
        {
            return await Pages(context, listings[0], cancel);
        }
        catch (HttpRequestException exception) when (listings.Length > 1 &&
                                                     exception.StatusCode == HttpStatusCode.NotFound)
        {
            return await Pages(context, listings[1], cancel);
        }
    }

    /// <summary>
    /// Page 1 of the listing discovery reads, most recently pushed first, with each repository's
    /// pushed_at as its token. It shares discovery's ETag, so while nothing is pushed it is a 304
    /// that costs nothing against the hourly limit, and a repository pushed to is fetched at once
    /// rather than at its idle cap.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        var listings = Listings(context);
        // With an owner named, the cached listing is the one discovery settled on; asking the
        // organization first every time would pay its 404 on every probe of a user.
        var listing = listings.FirstOrDefault(_ => context.Http.IsCached($"{_}&page=1")) ?? listings[0];
        var repositories = await context.Http.Get($"{listing}&page=1", GitHubContext.Default.ListGitHubRepository, cancel);
        return repositories.ToImmutableDictionary(_ => _.FullName, _ => $"{_.PushedAt:O}");
    }

    static async Task<List<GitHubRepository>> Pages(ProviderContext context, string path, Cancel cancel)
    {
        var all = new List<GitHubRepository>();
        for (var page = 1; page <= maxPages; page++)
        {
            var repositories = await context.Http.Get($"{path}&page={page}", GitHubContext.Default.ListGitHubRepository, cancel);
            all.AddRange(repositories);
            if (repositories.Count < 100)
            {
                break;
            }
        }

        return all;
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var started = Stopwatch.GetTimestamp();
        var repositories = pipelines.GroupBy(_ => _.RepoName).ToList();
        var readOnlyRepositories = context.Memory.TryGet<ImmutableHashSet<string>>(readOnly, out var remembered) ? remembered : [];
        var perRepository = await Concurrently.Map(
            repositories,
            async (repository, token) =>
            {
                var change = !readOnlyRepositories.Contains(repository.Key);
                var byWorkflow = repository.ToDictionary(_ => long.Parse(_.Id[(_.Id.LastIndexOf('/') + 1)..]));
                var count = Math.Min(100, perPipeline * byWorkflow.Count);
                // No created filter for the history limit: a run keeps its created_at through a re-run,
                // so one created before the cutoff and still running, or re-run since, would never
                // be returned. per_page bounds the response instead.
                var runs = await context.Http.Get(
                    $"repos/{repository.Key}/actions/runs?per_page={count}",
                    GitHubContext.Default.GitHubRuns,
                    token);
                var builds = new List<Build>();
                var taken = new Dictionary<long, int>();
                foreach (var run in runs.WorkflowRuns)
                {
                    if (!byWorkflow.TryGetValue(run.WorkflowId, out var pipeline))
                    {
                        continue;
                    }

                    // A run whose every job was skipped by an `if` did nothing, and showing it as the
                    // pipeline's latest hides the last run that actually built something.
                    if (run.Conclusion == "skipped")
                    {
                        continue;
                    }

                    taken.TryGetValue(run.WorkflowId, out var soFar);
                    if (soFar >= perPipeline)
                    {
                        continue;
                    }

                    taken[run.WorkflowId] = soFar + 1;
                    builds.Add(Convert(context.Connection.Id, repository.Key, pipeline, run, change));
                }

                return builds;
            },
            cancel,
            context.Progress);
        var all = perRepository.SelectMany(_ => _).ToList();
        // The poller fetches a repository at a time and logs each cycle; this is only for detail.
        Log.Debug(
            "GitHub fetch: {Repositories} repositories, {Builds} runs, {Elapsed:0.0}s",
            repositories.Count,
            all.Count,
            Stopwatch.GetElapsedTime(started).TotalSeconds);
        return all;
    }

    static Build Convert(string connectionId, string repository, Pipeline pipeline, GitHubRun run, bool change)
    {
        var status = run.Status switch
        {
            "in_progress" => BuildStatus.Running,
            "queued" or "waiting" or "pending" or "requested" => BuildStatus.Queued,
            "completed" => run.Conclusion switch
            {
                "success" => BuildStatus.Succeeded,
                "failure" or "timed_out" or "startup_failure" => BuildStatus.Failed,
                "cancelled" => BuildStatus.Cancelled,
                _ => BuildStatus.Unknown
            },
            _ => BuildStatus.Unknown
        };
        var pullRequest = run.PullRequests.FirstOrDefault();
        var web = $"https://github.com/{repository}";
        var branchWeb = string.IsNullOrEmpty(run.HeadRepository?.FullName) ? web : $"https://github.com/{run.HeadRepository.FullName}";
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            repository,
            run.HeadBranch,
            run.RunNumber.ToString(),
            status,
            run.Status == "completed" ? run.Conclusion : run.Status,
            run.CreatedAt,
            run.RunStartedAt ?? run.CreatedAt,
            run.Status == "completed" ? run.UpdatedAt : null,
            null,
            run.HtmlUrl,
            run.HeadBranch is null ? null : $"{branchWeb}/tree/{run.HeadBranch}",
            pullRequest?.Number.ToString(),
            pullRequest is null ? null : $"{web}/pull/{pullRequest.Number}",
            run.HeadSha,
            run.HeadCommit?.Message ?? run.DisplayTitle,
            run.Actor?.Login,
            CanRetry: change && run.Status == "completed",
            CanCancel: change && run.Status != "completed",
            Join(repository, run.Id.ToString(), run.Conclusion),
            web);
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var verb = parts[2] is "failure" or "timed_out" ? "rerun-failed-jobs" : "rerun";
        return context.Http.Send(HttpMethod.Post, $"repos/{parts[0]}/actions/runs/{parts[1]}/{verb}", null, cancel);
    }

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Send(HttpMethod.Post, $"repos/{parts[0]}/actions/runs/{parts[1]}/cancel", null, cancel);
    }

    /// <summary>
    /// The logs of the latest attempt's jobs that failed or timed out, rather than every job's, so
    /// the one that broke is not buried under the ones that passed. A run that failed before it
    /// started a job, as one with a broken workflow file does, has none. Each log is a redirect to
    /// storage that wants no credential, which the handler follows.
    /// </summary>
    public override async Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var failed = new List<GitHubJob>();
        for (var page = 1; page <= maxPages; page++)
        {
            var jobs = await context.Http.Get(
                $"repos/{parts[0]}/actions/runs/{parts[1]}/jobs?filter=latest&per_page=100&page={page}",
                GitHubContext.Default.GitHubJobs,
                cancel);
            failed.AddRange(jobs.Jobs.Where(_ => _.Conclusion is "failure" or "timed_out"));
            if (jobs.Jobs.Count < 100)
            {
                break;
            }
        }

        var logs = new List<(string Name, string Log)>();
        foreach (var job in failed)
        {
            logs.Add((job.Name, await context.Http.GetLog($"repos/{parts[0]}/actions/jobs/{job.Id}/logs", cancel)));
        }

        return Sections(logs);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("user", GitHubContext.Default.GitHubUser, cancel);
        return new(true, $"Signed in as {user.Login}", await AccessOrUnknown(context, cancel));
    }

    /// <summary>
    /// An OAuth App's token and a classic personal access token list their scopes in
    /// X-OAuth-Scopes. GitHub documents the header for no other token, so a response without it
    /// says nothing, as for a fine grained token. Re-running and cancelling a run need the repo
    /// scope, and with none of the repository scopes a token reads public information only.
    /// public_repo alone is left unknown: the documentation names only repo, so hiding the buttons
    /// could hide what works.
    /// </summary>
    public override async Task<BuildAccess> Access(ProviderContext context, Cancel cancel)
    {
        var header = await context.Http.GetHeader("user", "X-OAuth-Scopes", cancel);
        if (header is null)
        {
            return BuildAccess.Unknown;
        }

        var scopes = header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (scopes.Contains("repo"))
        {
            return BuildAccess.Change;
        }

        if (scopes.Contains("public_repo"))
        {
            return BuildAccess.Unknown;
        }

        return BuildAccess.Watch;
    }
}
