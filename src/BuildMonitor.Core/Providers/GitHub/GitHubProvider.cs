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
    static TimeSpan activeWindow = TimeSpan.FromDays(90);

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
    /// github.com for api.github.com, and an enterprise server's own root, under which it serves the
    /// API as well as the repositories.
    /// </summary>
    public override Uri RepositoryRoot(Connection connection)
    {
        var address = BaseAddress(connection);
        if (address.Host == "api.github.com")
        {
            return new("https://github.com/");
        }

        return new(address, "/");
    }

    /// <summary>
    /// How often every active repository's workflows are listed, however long since it was pushed
    /// to: enabling or disabling a workflow probably does not move pushed_at.
    /// </summary>
    static TimeSpan listEverything = TimeSpan.FromHours(1);

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
                        (context.ShowForksAndCollaborations || !_.Fork) &&
                        // Here rather than after discovery: listing an excluded repository's
                        // workflows costs a request the secondary limit counts, and an ignored org
                        // is dozens of them a poll, spent to produce rows the poller drops.
                        !Filters.ExcludesRepo(context.Filters, _.FullName))
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
                        $"{repository.HtmlUrl}/actions/workflows/{Path.GetFileName(_.Path)}",
                        repository.HtmlUrl))
                    .ToList();
            },
            cancel,
            context.Progress);
        var next = ImmutableDictionary.CreateBuilder<string, (string Pushed, List<Pipeline> Pipelines)>();
        for (var index = 0; index < stale.Count; index++)
        {
            next[stale[index].FullName] = (Pushed(stale[index]), perRepository[index]);
        }

        // In the listing's order, most recently pushed first, whether listed now or remembered. The
        // default branch from this listing rather than the remembered one: renaming it need not
        // move pushed_at, and a workflow listing is remembered until pushed_at moves.
        var pipelines = new List<Pipeline>();
        foreach (var repository in active)
        {
            if (!next.TryGetValue(repository.FullName, out var listed))
            {
                listed = known[repository.FullName];
                next[repository.FullName] = listed;
            }

            pipelines.AddRange(listed.Pipelines.Select(_ => _ with { DefaultBranch = repository.DefaultBranch }));
        }

        context.Memory.Set(listedWorkflows, next.ToImmutable());
        if (everything)
        {
            context.Memory.Set(listedEverything, now);
        }

        Log.Information(
            "GitHub discovery: {Repositories} repositories, {Active} pushed in the last {Days} days and not excluded, {Listed} of those listed, {Pipelines} workflows, {Elapsed:0.0}s",
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
                // A repository deleted since discovery answers with a 404, which would fail the
                // whole fetch; it has no builds until the next discovery drops it.
                var runs = await GetOrNone(
                    context,
                    $"repos/{repository.Key}/actions/runs?per_page={count}",
                    GitHubContext.Default.GitHubRuns,
                    token);
                var builds = new List<Build>();
                if (runs is null)
                {
                    return builds;
                }

                var taken = new Dictionary<long, int>();
                // The workflows with a run on their default branch among those kept, which is the
                // run a workflow's row is.
                var own = new HashSet<long>();
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

                    var build = Convert(context.Connection.Id, repository.Key, pipeline, run, change);
                    var onDefault = build.Branch is not null &&
                                    build.Branch == pipeline.DefaultBranch;
                    taken.TryGetValue(run.WorkflowId, out var soFar);
                    // Past the cap only for the workflow's first run on its default branch: a batch
                    // of pull requests fills a workflow's share of the page, and its own run, in the
                    // response already, would have been dropped for them.
                    if (soFar >= perPipeline &&
                        (!onDefault || own.Contains(run.WorkflowId)))
                    {
                        continue;
                    }

                    taken[run.WorkflowId] = soFar + 1;
                    if (onDefault)
                    {
                        own.Add(run.WorkflowId);
                    }

                    builds.Add(build);
                }

                foreach (var (id, pipeline) in byWorkflow)
                {
                    if (pipeline.DefaultBranch is { } defaultBranch &&
                        !own.Contains(id) &&
                        await DefaultRun(context, repository.Key, id, pipeline, defaultBranch, perPipeline, change, token) is { } run)
                    {
                        builds.Add(run);
                    }
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

    /// <summary>
    /// A workflow's newest run on the default branch, where the repository's page of runs had none:
    /// its share of the page went to pull requests, or it runs on the default branch rarely, as a
    /// scheduled or dispatched workflow does. Asked of the workflow's own runs rather than the
    /// repository's, whose default branch the scheduled and issue-triggered workflows would fill.
    /// The branch filter matches a fork's branch of the same name too, so a fork's main is passed
    /// over. A workflow with none since the history cutoff, as one only pull requests trigger has,
    /// is not asked again for an hour.
    /// </summary>
    static async Task<Build?> DefaultRun(ProviderContext context, string repository, long workflowId, Pipeline pipeline, string branch, int perPipeline, bool change, Cancel cancel)
    {
        var memory = context.Memory;
        var now = DateTimeOffset.UtcNow;
        if (DefaultRunMemory.KnownNone(memory, pipeline.Id, branch, now))
        {
            return null;
        }

        var runs = await GetOrNone(
            context,
            $"repos/{repository}/actions/workflows/{workflowId}/runs?branch={Encode(branch)}&per_page={perPipeline}",
            GitHubContext.Default.GitHubRuns,
            cancel);
        foreach (var run in runs?.WorkflowRuns ?? [])
        {
            if (run.Conclusion == "skipped")
            {
                continue;
            }

            var build = Convert(context.Connection.Id, repository, pipeline, run, change);
            if (build.Branch != branch)
            {
                continue;
            }

            if (context.Since is { } since &&
                !HistoryCutoff.Keeps(build, since))
            {
                break;
            }

            DefaultRunMemory.Found(memory, pipeline.Id, branch);
            return build;
        }

        DefaultRunMemory.None(memory, pipeline.Id, branch, now);
        return null;
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
        // The repository's page as its listing gave it, on the host the API names, which is not
        // github.com for GitHub Enterprise. Composed on github.com only for a pipeline without one.
        var web = pipeline.RepoUrl ?? $"https://github.com/{repository}";
        // The branch's page is in the repository it lives in, a fork for a pull request from one.
        var headRepository = run.HeadRepository;
        var branchWeb = web;
        if (headRepository is {HtmlUrl.Length: > 0})
        {
            branchWeb = headRepository.HtmlUrl;
        }
        else if (headRepository is {FullName.Length: > 0} &&
                 Uri.TryCreate(web, UriKind.Absolute, out var page))
        {
            // Named without its page, it is on the repository's host.
            branchWeb = $"{page.GetLeftPart(UriPartial.Authority)}/{headRepository.FullName}";
        }

        // A fork's branch behind its owner, since a fork's main is not this repository's main.
        string? branch = null;
        if (run.HeadBranch is { } head)
        {
            branch = PullRequestBranches.Head(head, PullRequestBranches.ForkOwner(headRepository?.FullName, repository));
        }

        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            repository,
            branch,
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
            pipeline.Url,
            web,
            DefaultBranch: pipeline.DefaultBranch);
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

    /// <summary>
    /// Every artifact the run uploaded. GitHub serves each as a zip whatever went into it, so the
    /// name it lists gains the extension the saved file will actually have.
    /// <para>
    /// An artifact past its retention is still listed, marked expired, and answers a download with
    /// a 410. It is carried as unavailable rather than left out, so a prompt can say the evidence
    /// existed and is gone rather than reading as a run that published nothing.
    /// </para>
    /// </summary>
    public override async Task<IReadOnlyList<BuildArtifact>> ListArtifacts(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var artifacts = new List<BuildArtifact>();
        for (var page = 1; page <= maxPages; page++)
        {
            var listed = await context.Http.Get(
                $"repos/{parts[0]}/actions/runs/{parts[1]}/artifacts?per_page=100&page={page}",
                GitHubContext.Default.GitHubArtifacts,
                cancel);
            artifacts.AddRange(listed.Artifacts.Select(_ => new BuildArtifact(
                _.Id.ToString(),
                $"{_.Name}.zip",
                _.SizeInBytes,
                _.Expired ? "expired" : null)));
            if (listed.Artifacts.Count < 100)
            {
                break;
            }
        }

        return artifacts;
    }

    /// <summary>
    /// The zip redirects to storage that carries its own signature in the URL, which the handler
    /// follows without the Authorization header. That is what makes it work rather than a problem
    /// to solve: the blob store refuses a credential it does not know.
    /// </summary>
    public override Task<long> DownloadArtifact(ProviderContext context, Build build, BuildArtifact artifact, Stream destination, long maxBytes, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Download($"repos/{parts[0]}/actions/artifacts/{artifact.Id}/zip", destination, maxBytes, cancel);
    }

    /// <summary>
    /// What became of a failed branch of a repository on this connection's host, whichever service
    /// built it. A pull request is asked for its own state. A fork's branch is asked for the newest
    /// pull request from it, since a fork's branch builds here only through one. Any other branch is
    /// asked whether it is still there, and then whether a tag of its name is, as a release
    /// workflow's run is named for its tag. The tags answer the question a missing branch's 404
    /// leaves open, whether the credential can see the repository at all: a repository it cannot see
    /// is a 404 there too, where one it can lists none.
    /// <para>
    /// A pull request the credential cannot see is a 404, and says nothing. A fine grained token
    /// without Pull requests or Contents read is refused with a 403, which the poller takes the same
    /// way rather than as a credential gone bad.
    /// </para>
    /// </summary>
    public override async Task<BranchFate> FateOf(ProviderContext context, BranchQuestion question, Cancel cancel)
    {
        if (RepositoryPath(context, question.Repository) is not { } repository ||
            repository.Count(_ => _ == '/') != 1)
        {
            return BranchFate.Unknown;
        }

        if (question.PullRequest is { } number)
        {
            var pullRequest = await GetOrNone(context, $"repos/{repository}/pulls/{Encode(number)}", GitHubContext.Default.GitHubPullRequest, cancel);
            return FateOf(pullRequest);
        }

        if (question.Branch.Contains(':'))
        {
            var fromFork = await GetOrNone(context, $"repos/{repository}/pulls?head={Encode(question.Branch)}&state=all&per_page=1", GitHubContext.Default.ListGitHubPullRequest, cancel);
            return FateOf(fromFork?.FirstOrDefault());
        }

        if (await GetOrNone(context, $"repos/{repository}/branches/{EncodePath(question.Branch)}", GitHubContext.Default.GitHubBranch, cancel) is not null)
        {
            return BranchFate.Open;
        }

        var tags = await GetOrNone(context, $"repos/{repository}/git/matching-refs/tags/{EncodePath(question.Branch)}", GitHubContext.Default.ListGitHubRef, cancel);
        if (tags is null)
        {
            return BranchFate.Unknown;
        }

        // Matching refs are every ref that starts with the name, so v1 lists v1.1 too.
        if (tags.Any(_ => _.Ref == $"refs/tags/{question.Branch}"))
        {
            return BranchFate.Open;
        }

        return BranchFate.Deleted;
    }

    static BranchFate FateOf(GitHubPullRequest? pullRequest)
    {
        if (pullRequest is null)
        {
            return BranchFate.Unknown;
        }

        if (pullRequest.State == "open")
        {
            return BranchFate.Open;
        }

        if (pullRequest.MergedAt is not null)
        {
            return BranchFate.Merged;
        }

        return BranchFate.Closed;
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
