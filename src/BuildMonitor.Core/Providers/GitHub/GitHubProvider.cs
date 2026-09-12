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

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var repositories = await Repositories(context, cancel);
        var pipelines = new List<Pipeline>();
        var cutoff = DateTimeOffset.UtcNow - activeWindow;
        foreach (var repository in repositories.Where(_ => !_.Archived && !_.Disabled && _.PushedAt > cutoff))
        {
            var workflows = await context.Http.Get(
                $"repos/{repository.FullName}/actions/workflows?per_page=100",
                GitHubContext.Default.GitHubWorkflows,
                cancel);
            pipelines.AddRange(workflows.Workflows
                .Where(_ => _.State == "active")
                .Select(_ => new Pipeline(
                    $"{repository.FullName}/{_.Id}",
                    _.Name,
                    repository.FullName,
                    repository.FullName,
                    $"{repository.HtmlUrl}/actions/workflows/{Path.GetFileName(_.Path)}")));
        }

        return pipelines;
    }

    static async Task<List<GitHubRepository>> Repositories(ProviderContext context, Cancel cancel)
    {
        var owner = context.Scope("owner");
        if (owner.Length == 0)
        {
            return await Pages(context, "user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member", cancel);
        }

        try
        {
            return await Pages(context, $"orgs/{Encode(owner)}/repos?per_page=100&sort=pushed&type=all", cancel);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return await Pages(context, $"users/{Encode(owner)}/repos?per_page=100&sort=pushed", cancel);
        }
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
        var builds = new List<Build>();
        foreach (var repository in pipelines.GroupBy(_ => _.RepoName))
        {
            var byWorkflow = repository.ToDictionary(_ => long.Parse(_.Id[(_.Id.LastIndexOf('/') + 1)..]));
            var count = Math.Min(100, perPipeline * byWorkflow.Count);
            var runs = await context.Http.Get(
                $"repos/{repository.Key}/actions/runs?per_page={count}",
                GitHubContext.Default.GitHubRuns,
                cancel);
            var taken = new Dictionary<long, int>();
            foreach (var run in runs.WorkflowRuns)
            {
                if (!byWorkflow.TryGetValue(run.WorkflowId, out var pipeline))
                {
                    continue;
                }

                taken.TryGetValue(run.WorkflowId, out var soFar);
                if (soFar >= perPipeline)
                {
                    continue;
                }

                taken[run.WorkflowId] = soFar + 1;
                builds.Add(Convert(context.Connection.Id, repository.Key, pipeline, run));
            }
        }

        return builds;
    }

    static Build Convert(string connectionId, string repository, Pipeline pipeline, GitHubRun run)
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
            run.HeadBranch is null ? null : $"{web}/tree/{run.HeadBranch}",
            pullRequest?.Number.ToString(),
            pullRequest is null ? null : $"{web}/pull/{pullRequest.Number}",
            run.HeadSha,
            run.HeadCommit?.Message ?? run.DisplayTitle,
            run.Actor?.Login,
            CanRetry: run.Status == "completed",
            CanCancel: run.Status != "completed",
            Join(repository, run.Id.ToString(), run.Conclusion));
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

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("user", GitHubContext.Default.GitHubUser, cancel);
        return new(true, $"Signed in as {user.Login}");
    }
}
