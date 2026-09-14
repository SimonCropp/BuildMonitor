/// <summary>
/// https://www.appveyor.com/docs/api/projects-builds/
/// </summary>
sealed class AppVeyorProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.AppVeyor;

    /// <summary>
    /// A v2 (user level) token works across accounts, so a call must name the account. Most name
    /// it through this prefix, but history and cancel carry it in their own path and have no
    /// prefixed route: AppVeyor answers one with a 200 carrying its web app's HTML, which failed
    /// to parse for every project.
    /// </summary>
    static string Prefix(ProviderContext context)
    {
        var account = context.Scope("account");
        return account.Length == 0 ? "api" : $"api/account/{Encode(account)}";
    }

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var projects = await context.Http.Get($"{Prefix(context)}/projects", AppVeyorContext.Default.ListAppVeyorProject, cancel);
        return projects
            .Select(_ => new Pipeline(
                $"{_.AccountName}/{_.Slug}",
                _.Name,
                _.RepositoryName ?? _.Name,
                _.RepositoryType,
                $"https://ci.appveyor.com/project/{_.AccountName}/{_.Slug}"))
            .ToList();
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        foreach (var pipeline in pipelines)
        {
            var history = await context.Http.Get(
                $"api/projects/{pipeline.Id}/history?recordsNumber={perPipeline}",
                AppVeyorContext.Default.AppVeyorHistory,
                cancel);
            builds.AddRange(history.Builds.Select(_ => Convert(context.Connection.Id, pipeline, _)));
        }

        return builds;
    }

    /// <summary>
    /// The projects list, which carries each project's latest build, with that build's id, status
    /// and last update as the token. AppVeyor sends no ETags, so without this every quiet project
    /// would cost a full history request each time its schedule came round just to find nothing
    /// new; one list a minute finds the projects that changed. Builds on other branches that start
    /// while a newer one exists wait for the schedule.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        var projects = await context.Http.Get($"{Prefix(context)}/projects", AppVeyorContext.Default.ListAppVeyorProject, cancel);
        var tokens = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var project in projects)
        {
            if (project.Builds.FirstOrDefault() is { } build)
            {
                tokens[$"{project.AccountName}/{project.Slug}"] = $"{build.BuildId}|{build.Status}|{build.Updated?.ToString("O", CultureInfo.InvariantCulture)}";
            }
        }

        return tokens.ToImmutable();
    }

    static Build Convert(string connectionId, Pipeline pipeline, AppVeyorBuild build)
    {
        var status = build.Status switch
        {
            "queued" => BuildStatus.Queued,
            "running" or "cancelling" => BuildStatus.Running,
            "success" => BuildStatus.Succeeded,
            "failed" => BuildStatus.Failed,
            "cancelled" => BuildStatus.Cancelled,
            _ => BuildStatus.Unknown
        };
        var github = string.Equals(pipeline.Group, "gitHub", StringComparison.OrdinalIgnoreCase);
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            build.Branch,
            build.BuildNumber.ToString(),
            status,
            build.Status,
            build.Created,
            build.Started,
            build.Finished,
            null,
            $"{pipeline.Url}/builds/{build.BuildId}",
            github && build.Branch is not null ? $"https://github.com/{pipeline.RepoName}/tree/{build.Branch}" : null,
            build.PullRequestId,
            github && build.PullRequestId is not null ? $"https://github.com/{pipeline.RepoName}/pull/{build.PullRequestId}" : null,
            build.CommitId,
            build.Message,
            build.AuthorName,
            CanRetry: status is BuildStatus.Failed or BuildStatus.Cancelled or BuildStatus.Succeeded,
            CanCancel: status is BuildStatus.Queued or BuildStatus.Running,
            Join(build.BuildId.ToString(), build.Version));
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var body = new AppVeyorRerun(long.Parse(parts[0]), false);
        return context.Http.Send(HttpMethod.Put, $"{Prefix(context)}/builds", HttpJson.Json(body, AppVeyorContext.Default.AppVeyorRerun), cancel);
    }

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var version = Split(build)[1];
        return context.Http.Send(HttpMethod.Delete, $"api/builds/{build.PipelineId}/{Encode(version)}", null, cancel);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var projects = await context.Http.Get($"{Prefix(context)}/projects", AppVeyorContext.Default.ListAppVeyorProject, cancel);
        return new(true, $"{projects.Count} projects");
    }
}
