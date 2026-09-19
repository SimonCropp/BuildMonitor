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
        // Excluded before the definitions are listed rather than after: a project costs a request
        // of its own, and an excluded one's definitions are only there to be thrown away.
        var projects = (await Projects(context, cancel))
            .Where(_ => !Filters.ExcludesRepo(context.Filters, _))
            .ToList();
        // Concurrently, as a project at a time kept a first poll waiting an estimated fifteen
        // seconds with fifty projects. The results keep the projects' order.
        var perProject = await Concurrently.Map(
            projects,
            async (project, token) =>
            {
                var definitions = await context.Http.Get($"{Encode(project)}/_apis/pipelines?{apiVersion}", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsPipeline, token);
                return definitions.Value
                    .Select(_ => new Pipeline(
                        $"{project}/{_.Id}",
                        _.Folder is null or "\\" ? _.Name : $"{_.Folder.Trim('\\')}\\{_.Name}",
                        project,
                        project,
                        _.Links?.Web?.Href ?? $"{context.Http.BaseAddress}{Encode(project)}/_build?definitionId={_.Id}"))
                    .ToList();
            },
            cancel);
        return perProject.SelectMany(_ => _).ToList();
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
            // Per definition rather than a total: $top filled with the busiest definitions and hid
            // the quiet ones of a project with more than a few dozen.
            // No minTime for the history limit: under queueTimeDescending it filters on queue time,
            // so a build queued before the cutoff and still running would never be returned.
            var response = await context.Http.Get(
                $"{Encode(project.Key)}/_apis/build/builds?definitions={string.Join(',', byDefinition.Keys)}&maxBuildsPerDefinition={perPipeline}&queryOrder=queueTimeDescending&properties={TriggeredBy.Property}&{apiVersion}",
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
                // Every build names the identity it was queued for, id and all, which is the only
                // place a name for that id is free. Another service handed the same id and no name,
                // as an Octopus release created by a pipeline is, reads it back from here.
                context.Identities.Add(build.RequestedFor?.Id, build.RequestedFor?.DisplayName);
                builds.Add(Convert(context, project.Key, pipeline, build));
            }
        }

        return builds;
    }

    /// <summary>
    /// Per project, the builds queued since the newest one seen, with that queue time as the
    /// token. Azure DevOps sends no ETags, so every check is billed; asked this way an unchanged
    /// project answers empty for about 0.002 throughput units, where a fetch costs about 0.07. The
    /// first probe asks for the single newest build, to have a time to ask after.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        // Concurrently, as the cycle plans nothing until the probe is back, and a project at a time
        // held it for an estimated fifteen seconds with fifty projects. The same throughput units.
        var newest = await Concurrently.Map(
            groups,
            async (group, token) =>
            {
                var definitions = string.Join(',', group.Pipelines.Select(_ => _.Id[(_.Id.LastIndexOf('/') + 1)..]));
                var since = previous.GetValueOrDefault(group.Key);
                var filter = since is null ? "$top=1" : $"minTime={Encode(since)}&$top=50";
                var response = await context.Http.Get(
                    $"{Encode(group.Key)}/_apis/build/builds?definitions={definitions}&{filter}&queryOrder=queueTimeDescending&{apiVersion}",
                    AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsBuild,
                    token);
                DateTimeOffset? latest = since is null ? null : DateTimeOffset.Parse(since, CultureInfo.InvariantCulture);
                foreach (var build in response.Value)
                {
                    var queued = build.QueueTime;
                    if (latest is null ||
                        queued > latest)
                    {
                        latest = queued ?? latest;
                    }
                }

                return latest;
            },
            cancel);
        var tokens = ImmutableDictionary.CreateBuilder<string, string>();
        for (var index = 0; index < groups.Length; index++)
        {
            if (newest[index] is { } value)
            {
                tokens[groups[index].Key] = value.ToString("O", CultureInfo.InvariantCulture);
            }
        }

        return tokens.ToImmutable();
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
            Author(context, build),
            CanRetry: build.Status == "completed",
            CanCancel: build.Status is "inProgress" or "notStarted" or "postponed",
            Join(project, build.Id.ToString()),
            $"{context.Http.BaseAddress}{Encode(project)}");
    }

    /// <summary>
    /// Who the row names: whoever <see cref="TriggeredBy"/> says the build is for, then whoever it
    /// was queued for.
    /// </summary>
    static string? Author(ProviderContext context, AzureDevOpsBuild build) =>
        TriggeredBy.Author(context, build.Property(TriggeredBy.Property), $"Build {build.Id}") ??
        build.RequestedFor?.DisplayName;

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

    /// <summary>
    /// The logs of the failed tasks, each under its job, from the build's timeline. A job that failed
    /// with no task failing, as one whose agent was lost does, gives its own log instead. Asked for
    /// as plain text: accepting JSON, a log comes back as an array of its lines, or not at all.
    /// </summary>
    public override async Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var builds = $"{Encode(parts[0])}/_apis/build/builds/{parts[1]}";
        var timeline = await context.Http.Get($"{builds}/timeline?{apiVersion}", AzureDevOpsContext.Default.AzureDevOpsTimeline, cancel);
        var byId = timeline.Records.ToDictionary(_ => _.Id);
        var failed = timeline.Records.Where(_ => _ is { Result: "failed", Log: not null }).ToList();
        var tasks = failed.Where(_ => _.Type == "Task").ToList();
        var chosen = tasks.Count > 0 ? tasks : failed.Where(_ => _.Type == "Job").ToList();
        var logs = new List<(string Name, string Log)>();
        // Log ids are handed out as the logs are written, so this is the order the build ran in.
        foreach (var record in chosen.OrderBy(_ => _.Log!.Id))
        {
            var name = record is
                       {
                           Type: "Task",
                           ParentId: { } parent
                       } &&
                       byId.TryGetValue(parent, out var job)
                ? $"{job.Name} / {record.Name}"
                : record.Name ?? "";
            logs.Add((name, await context.Http.GetLog($"{builds}/logs/{record.Log!.Id}?{apiVersion}", cancel, "text/plain")));
        }

        return Sections(logs);
    }

    /// <summary>
    /// The build's published artifacts, each fetched as a zip whatever kind it is.
    /// <para>
    /// The size comes from a property the documented schema does not mention, so an artifact whose
    /// resource does not carry it is listed with no size and held to the per file cap instead.
    /// </para>
    /// </summary>
    public override async Task<IReadOnlyList<BuildArtifact>> ListArtifacts(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var listed = await context.Http.Get(
            $"{Encode(parts[0])}/_apis/build/builds/{parts[1]}/artifacts?{apiVersion}",
            AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsArtifact,
            cancel);
        return listed.Value
            // The name is what the download is asked for by, so it is the id as well as the label.
            .Select(_ => new BuildArtifact(_.Name, $"{_.Name}.zip", _.Bytes()))
            .ToList();
    }

    /// <summary>
    /// Through the connection's own address with <c>$format=zip</c> rather than the resource's
    /// downloadUrl, which points at a separate artifacts host where the handler drops the
    /// credential and the request is refused.
    /// </summary>
    public override Task<long> DownloadArtifact(ProviderContext context, Build build, BuildArtifact artifact, Stream destination, long maxBytes, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Download(
            $"{Encode(parts[0])}/_apis/build/builds/{parts[1]}/artifacts?artifactName={Encode(artifact.Id)}&$format=zip&{apiVersion}",
            destination,
            maxBytes,
            cancel);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var projects = await context.Http.Get($"_apis/projects?{apiVersion}&$top=100", AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsProject, cancel);
        return new(true, $"{projects.Value.Count} projects");
    }
}
