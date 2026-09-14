/// <summary>
/// https://www.jetbrains.com/help/teamcity/rest/teamcity-rest-api-documentation.html
/// <para>
/// One request fetches the latest builds of every configuration, rather than one request per
/// configuration: a server with a few hundred build configurations would otherwise cost a few
/// hundred calls a poll. The builds are asked for per configuration inside that request. A flat
/// list of recent builds puts queued builds first, so a long queue filled it and hid every
/// configuration that had not built lately.
/// </para>
/// </summary>
sealed class TeamCityProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.TeamCity;

    const string buildFields = "build(id,number,status,state,branchName,defaultBranch,webUrl,statusText,queuedDate,startDate,finishDate,buildTypeId,canceledInfo(text),running-info(percentageComplete,elapsedSeconds,estimatedTotalSeconds,leftSeconds),triggered(user(username,name)),revisions(revision(version)))";

    public override Uri BaseAddress(Connection connection) =>
        new(base.BaseAddress(connection), "app/rest/");

    static string Project(ProviderContext context)
    {
        var project = context.Scope("project");
        if (project.Length == 0)
        {
            return "_Root";
        }

        return project;
    }

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var types = await context.Http.Get(
            $"buildTypes?locator=affectedProject:(id:{Encode(Project(context))})&fields=buildType(id,name,projectName,projectId,webUrl)",
            TeamCityContext.Default.TeamCityBuildTypes,
            cancel);
        return types.BuildType
            .Select(_ => new Pipeline(_.Id, $"{_.ProjectName} / {_.Name}", _.ProjectName, _.ProjectId, _.WebUrl))
            .ToList();
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var byType = pipelines.ToDictionary(_ => _.Id);
        var response = await context.Http.Get(
            $"buildTypes?locator=affectedProject:(id:{Encode(Project(context))})&fields=buildType(id,builds($locator(branch:default:any,state:any,canceled:any,failedToStart:any,count:{perPipeline}),{buildFields}))",
            TeamCityContext.Default.TeamCityBuildTypes,
            cancel);
        var builds = new List<Build>();
        foreach (var type in response.BuildType)
        {
            if (!byType.TryGetValue(type.Id, out var pipeline))
            {
                continue;
            }

            builds.AddRange((type.Builds?.Build ?? []).Take(perPipeline).Select(_ => Convert(context.Connection.Id, pipeline, _)));
        }

        return builds;
    }

    static Build Convert(string connectionId, Pipeline pipeline, TeamCityBuild build)
    {
        var status = build.State switch
        {
            "queued" => BuildStatus.Queued,
            "running" => BuildStatus.Running,
            _ when build.CanceledInfo is not null => BuildStatus.Cancelled,
            _ => build.Status switch
            {
                "SUCCESS" => BuildStatus.Succeeded,
                "FAILURE" or "ERROR" => BuildStatus.Failed,
                _ => BuildStatus.Unknown
            }
        };
        ProviderEstimate? estimate = null;
        if (build.RunningInfo is { } running)
        {
            estimate = new(
                running.EstimatedTotalSeconds is > 0 ? TimeSpan.FromSeconds(running.EstimatedTotalSeconds.Value) : null,
                running.PercentageComplete,
                running.LeftSeconds is { } left ? TimeSpan.FromSeconds(left) : null);
        }

        var pullRequest = PullRequest(build.BranchName);
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            build.BranchName,
            build.Number ?? build.Id.ToString(),
            status,
            build.State != "finished" ? build.State : status == BuildStatus.Cancelled ? "canceled" : build.Status?.ToLowerInvariant(),
            TeamCityDate.Parse(build.QueuedDate),
            TeamCityDate.Parse(build.StartDate) ?? TeamCityDate.Parse(build.QueuedDate),
            TeamCityDate.Parse(build.FinishDate),
            estimate,
            build.WebUrl,
            null,
            pullRequest,
            null,
            build.Revisions?.Revision.FirstOrDefault()?.Version,
            build.StatusText,
            build.Triggered?.User?.Name ?? build.Triggered?.User?.Username,
            CanRetry: build.State == "finished",
            CanCancel: build.State is "queued" or "running",
            Join(build.State, build.Id.ToString(), build.BuildTypeId, build.BranchName));
    }

    /// <summary>
    /// The pull request branch specs TeamCity's VCS features create: pull/123, or 123/merge.
    /// </summary>
    static string? PullRequest(string? branch)
    {
        if (branch is null)
        {
            return null;
        }

        if (branch.StartsWith("pull/", StringComparison.Ordinal) &&
            int.TryParse(branch.AsSpan(5), out var number))
        {
            return number.ToString();
        }

        var slash = branch.IndexOf('/');
        if (slash > 0 &&
            branch.EndsWith("/merge", StringComparison.Ordinal) &&
            int.TryParse(branch.AsSpan(0, slash), out var merge))
        {
            return merge.ToString();
        }

        return null;
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var body = new TeamCityQueueRequest(new(parts[2]), parts[3].Length == 0 ? null : parts[3]);
        return context.Http.Send(HttpMethod.Post, "buildQueue", HttpJson.Json(body, TeamCityContext.Default.TeamCityQueueRequest), cancel);
    }

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var body = HttpJson.Json(new("Cancelled from BuildMonitor", false), TeamCityContext.Default.TeamCityCancel);
        var path = parts[0] == "queued" ? $"buildQueue/id:{parts[1]}" : $"builds/id:{parts[1]}";
        return context.Http.Send(HttpMethod.Post, path, body, cancel);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var server = await context.Http.Get("server", TeamCityContext.Default.TeamCityServer, cancel);
        return new(true, $"TeamCity {server.Version}");
    }
}
