/// <summary>
/// https://learn.microsoft.com/en-us/rest/api/azure/devops/build/builds/list
/// </summary>
sealed class AzureDevOpsProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.AzureDevOps;

    const string apiVersion = "api-version=7.1";

    /// <summary>
    /// The organization is part of the path: https://dev.azure.com/{organization}/, or the
    /// collection of an on premises server.
    /// </summary>
    public override Uri BaseAddress(Connection connection) =>
        new(base.BaseAddress(connection), $"{Encode(connection.ScopeValue("organization"))}/");

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var projects = await Projects(context, cancel);
        var pipelines = new List<Pipeline>();
        foreach (var project in projects)
        {
            var definitions = await context.Http.Get($"{Encode(project)}/_apis/pipelines?{apiVersion}", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsPipeline, cancel);
            pipelines.AddRange(definitions.Value.Select(_ => new Pipeline(
                $"{project}/{_.Id}",
                _.Folder is null or "\\" ? _.Name : $"{_.Folder.Trim('\\')}\\{_.Name}",
                project,
                project,
                _.Links?.Web?.Href ?? $"{context.Http.BaseAddress}{Encode(project)}/_build?definitionId={_.Id}")));
        }

        return pipelines;
    }

    static async Task<List<string>> Projects(ProviderContext context, Cancel cancel)
    {
        var project = context.Scope("project");
        if (project.Length > 0)
        {
            return [project];
        }

        var projects = await context.Http.Get($"_apis/projects?{apiVersion}&$top=100", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, cancel);
        return projects.Value.Select(_ => _.Name).ToList();
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        foreach (var project in pipelines.GroupBy(_ => _.RepoName))
        {
            var byDefinition = project.ToDictionary(_ => long.Parse(_.Id[(_.Id.LastIndexOf('/') + 1)..]));
            var top = Math.Min(200, perPipeline * byDefinition.Count);
            var response = await context.Http.Get(
                $"{Encode(project.Key)}/_apis/build/builds?definitions={string.Join(',', byDefinition.Keys)}&$top={top}&queryOrder=queueTimeDescending&{apiVersion}",
                AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsBuild,
                cancel);
            var taken = new Dictionary<long, int>();
            foreach (var build in response.Value)
            {
                if (build.Definition is null ||
                    !byDefinition.TryGetValue(build.Definition.Id, out var pipeline))
                {
                    continue;
                }

                taken.TryGetValue(build.Definition.Id, out var soFar);
                if (soFar >= perPipeline)
                {
                    continue;
                }

                taken[build.Definition.Id] = soFar + 1;
                builds.Add(Convert(context, project.Key, pipeline, build));
            }
        }

        return builds;
    }

    static Build Convert(ProviderContext context, string project, Pipeline pipeline, AzureDevOpsBuild build)
    {
        var status = build.Status switch
        {
            "inProgress" or "cancelling" => BuildStatus.Running,
            "notStarted" or "postponed" or "none" => BuildStatus.Queued,
            "completed" => build.Result switch
            {
                "succeeded" => BuildStatus.Succeeded,
                "partiallySucceeded" or "failed" => BuildStatus.Failed,
                "canceled" => BuildStatus.Cancelled,
                _ => BuildStatus.Unknown
            },
            _ => BuildStatus.Unknown
        };
        string? branch = null;
        string? pullRequest = null;
        if (build.SourceBranch is { } source)
        {
            if (source.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                branch = source["refs/heads/".Length..];
            }
            else if (source.StartsWith("refs/pull/", StringComparison.Ordinal))
            {
                pullRequest = source.Split('/')[2];
            }
            else
            {
                branch = source;
            }
        }

        var (branchUrl, pullRequestUrl) = RepositoryLinks(context, project, build.Repository, branch, pullRequest);
        return new(
            context.Connection.Id,
            pipeline.Id,
            pipeline.Name,
            build.Repository?.Name ?? project,
            branch,
            build.BuildNumber ?? build.Id.ToString(),
            status,
            build.Status == "completed" ? build.Result : build.Status,
            build.QueueTime,
            build.StartTime ?? build.QueueTime,
            build.FinishTime,
            null,
            build.Links?.Web?.Href ?? $"{context.Http.BaseAddress}{Encode(project)}/_build/results?buildId={build.Id}",
            branchUrl,
            pullRequest,
            pullRequestUrl,
            build.SourceVersion,
            build.TriggerInfo?.Message,
            build.RequestedFor?.DisplayName,
            CanRetry: build.Status == "completed",
            CanCancel: build.Status is "inProgress" or "notStarted" or "postponed",
            Join(project, build.Id.ToString()));
    }

    static (string? Branch, string? PullRequest) RepositoryLinks(ProviderContext context, string project, AzureDevOpsRepository? repository, string? branch, string? pullRequest)
    {
        if (repository is null)
        {
            return (null, null);
        }

        if (string.Equals(repository.Type, "GitHub", StringComparison.OrdinalIgnoreCase))
        {
            var web = $"https://github.com/{repository.Id}";
            return (branch is null ? null : $"{web}/tree/{branch}", pullRequest is null ? null : $"{web}/pull/{pullRequest}");
        }

        if (string.Equals(repository.Type, "TfsGit", StringComparison.OrdinalIgnoreCase))
        {
            var web = $"{context.Http.BaseAddress}{Encode(project)}/_git/{Encode(repository.Name ?? "")}";
            return (branch is null ? null : $"{web}?version=GB{Encode(branch)}", pullRequest is null ? null : $"{web}/pullrequest/{pullRequest}");
        }

        return (null, null);
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Send(HttpMethod.Patch, $"{Encode(parts[0])}/_apis/build/builds/{parts[1]}?retry=true&{apiVersion}", HttpJson.Json("{}"), cancel);
    }

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Send(HttpMethod.Patch, $"{Encode(parts[0])}/_apis/build/builds/{parts[1]}?{apiVersion}", HttpJson.Json("""{"status":"cancelling"}"""), cancel);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var projects = await context.Http.Get($"_apis/projects?{apiVersion}&$top=100", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, cancel);
        return new(true, $"{projects.Value.Count} projects");
    }
}
