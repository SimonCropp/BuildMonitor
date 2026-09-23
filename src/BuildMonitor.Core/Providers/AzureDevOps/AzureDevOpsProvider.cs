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
            var fetched = byDefinition.Keys.ToDictionary(_ => _, _ => new List<(AzureDevOpsBuild Raw, Build Build)>());
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
                fetched[build.Definition.Id].Add((build, Convert(context, project.Key, pipeline, build)));
            }

            // By default branch, so the definitions missing a build on the same one share a request.
            var missing = new Dictionary<string, List<long>>();
            var now = DateTimeOffset.UtcNow;
            foreach (var (id, runs) in fetched)
            {
                var pipeline = byDefinition[id];
                var defaultBranch = DefaultBranch(context, pipeline, runs);
                builds.AddRange(runs.Select(_ => _.Build with { DefaultBranch = defaultBranch }));
                if (defaultBranch is null ||
                    runs.Any(_ => _.Build.Branch == defaultBranch) ||
                    DefaultRunMemory.KnownNone(context.Memory, pipeline.Id, defaultBranch, now))
                {
                    continue;
                }

                if (!missing.TryGetValue(defaultBranch, out var definitions))
                {
                    definitions = [];
                    missing[defaultBranch] = definitions;
                }

                definitions.Add(id);
            }

            foreach (var (branch, definitions) in missing)
            {
                builds.AddRange(await DefaultRuns(context, project.Key, byDefinition, definitions, branch, cancel));
            }
        }

        return builds;
    }

    /// <summary>
    /// The branch a definition's own builds are on. The definition's repository names a default
    /// branch, but a GitHub-backed one keeps the name it had when the pipeline was made, and says
    /// master where the repository moved to main. The window is the witness: the branch its pull
    /// request builds target, or its other builds ran on, remembered from the last window that had
    /// pull requests in it for the windows that have none.
    /// </summary>
    static string? DefaultBranch(ProviderContext context, Pipeline pipeline, List<(AzureDevOpsBuild Raw, Build Build)> runs)
    {
        var key = $"default-branch|{pipeline.Id}";
        context.Memory.TryGet<string>(key, out var remembered);
        var targets = runs
            .Select(_ => _.Raw.Parameter("system.pullRequest.targetBranch"))
            .OfType<string>()
            .Select(HeadName)
            .ToList();
        var built = runs
            .Where(_ => _.Build.PullRequestNumber is null)
            .Select(_ => _.Build.Branch)
            .OfType<string>()
            .ToHashSet();
        var chosen = DefaultBranches.Choose([remembered], targets, built);
        if (targets.Count > 0 &&
            chosen is not null)
        {
            context.Memory.Set(key, chosen);
        }

        return chosen;
    }

    /// <summary>
    /// The newest build on the default branch of each definition the window left it out of: a
    /// burst of pull requests fills a definition's share. One request for the definitions sharing
    /// that branch, filtered on its ref, which leaves out the pull request builds targeting it. A
    /// definition with none since the history cutoff is not asked again for an hour.
    /// </summary>
    static async Task<List<Build>> DefaultRuns(ProviderContext context, string project, Dictionary<long, Pipeline> byDefinition, List<long> definitions, string branch, Cancel cancel)
    {
        var response = await context.Http.Get(
            $"{Encode(project)}/_apis/build/builds?definitions={string.Join(',', definitions)}&branchName={Encode($"refs/heads/{branch}")}&maxBuildsPerDefinition=1&queryOrder=queueTimeDescending&properties={TriggeredBy.Property}&{apiVersion}",
            AzureDevOpsContext.Default.AzureDevOpsListAzureDevOpsBuild,
            cancel);
        var now = DateTimeOffset.UtcNow;
        var found = new List<Build>();
        foreach (var id in definitions)
        {
            var pipeline = byDefinition[id];
            if (response.Value.FirstOrDefault(_ => _.Definition?.Id == id) is not { } raw)
            {
                DefaultRunMemory.None(context.Memory, pipeline.Id, branch, now);
                continue;
            }

            var build = Convert(context, project, pipeline, raw) with
            {
                DefaultBranch = branch
            };
            if (build.Branch != branch ||
                (context.Since is { } since && !HistoryCutoff.Keeps(build, since)))
            {
                DefaultRunMemory.None(context.Memory, pipeline.Id, branch, now);
                continue;
            }

            context.Identities.Add(raw.RequestedFor?.Id, raw.RequestedFor?.DisplayName);
            DefaultRunMemory.Found(context.Memory, pipeline.Id, branch);
            found.Add(build);
        }

        return found;
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
        // The branch its page is at, which a pull request from a fork has none of here.
        string? linked = null;
        if (build.SourceBranch is { } source)
        {
            if (source.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                branch = source["refs/heads/".Length..];
                linked = branch;
            }
            else if (source.StartsWith("refs/pull/", StringComparison.Ordinal))
            {
                pullRequest = source.Split('/')[2];
                linked = PullRequestSource(build);
                branch = linked ?? PullRequestBranches.Unnamed(pullRequest);
            }
            else
            {
                branch = source;
                linked = branch;
            }
        }

        var (repositoryWeb, branchUrl, pullRequestUrl) = RepositoryLinks(context, project, build.Repository, linked, pullRequest);
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
            pipeline.Url,
            repositoryWeb);
    }

    /// <summary>
    /// The branch a pull request build came from. Its source branch is the pull request's merge
    /// ref, which gave every pull request of a definition one empty branch, so one key and one row
    /// between them. Null for a pull request from a fork: Azure DevOps names neither the fork nor its
    /// owner, and the fork's branch bare would read as one of this repository's, most often main.
    /// </summary>
    static string? PullRequestSource(AzureDevOpsBuild build)
    {
        var fork = build.Parameter("system.pullRequest.isFork") ?? build.TriggerInfo?.IsFork;
        if (string.Equals(fork, "True", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var source = build.Parameter("system.pullRequest.sourceBranch") ?? build.TriggerInfo?.SourceBranch;
        if (source is not { Length: > 0 })
        {
            return null;
        }

        return HeadName(source);
    }

    /// <summary>
    /// A branch as the rows name it: GitHub sends the bare name, Azure Repos the ref.
    /// </summary>
    static string HeadName(string branch)
    {
        if (branch.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            return branch["refs/heads/".Length..];
        }

        return branch;
    }

    /// <summary>
    /// Who the row names: whoever <see cref="TriggeredBy"/> says the build is for, then whoever it
    /// was queued for.
    /// </summary>
    static string? Author(ProviderContext context, AzureDevOpsBuild build) =>
        TriggeredBy.Author(context, build.Property(TriggeredBy.Property), $"Build {build.Id}") ??
        build.RequestedFor?.DisplayName;

    /// <summary>
    /// The repository behind the build, and the branch and pull request pages on it. A pipeline may
    /// build a repository the server does not host, and only these two types give an address the
    /// build carries enough of to reach; for the rest the row's name is plain text rather than a
    /// link to the Azure DevOps project, which is not the repository.
    /// </summary>
    static (string? Repository, string? Branch, string? PullRequest) RepositoryLinks(ProviderContext context, string project, AzureDevOpsRepository? repository, string? branch, string? pullRequest)
    {
        if (repository is null)
        {
            return (null, null, null);
        }

        if (string.Equals(repository.Type, "GitHub", StringComparison.OrdinalIgnoreCase))
        {
            var web = $"https://github.com/{repository.Id}";
            return (web, branch is null ? null : $"{web}/tree/{branch}", pullRequest is null ? null : $"{web}/pull/{pullRequest}");
        }

        if (string.Equals(repository.Type, "TfsGit", StringComparison.OrdinalIgnoreCase))
        {
            var web = $"{context.Http.BaseAddress}{Encode(project)}/_git/{Encode(repository.Name ?? "")}";
            return (web, branch is null ? null : $"{web}?version=GB{Encode(branch)}", pullRequest is null ? null : $"{web}/pullrequest/{pullRequest}");
        }

        return (null, null, null);
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
    /// Moves the queued build to the front of the queue, which is what the run's own page calls
    /// Run next. Azure DevOps documents no call of its own for it, only the position as a field of
    /// the build that Update Build will take, so this writes the position the button is seen to
    /// leave behind rather than a verb.
    /// </summary>
    public override Task RunNext(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Send(HttpMethod.Patch, $"{Encode(parts[0])}/_apis/build/builds/{parts[1]}?{apiVersion}", HttpJson.Json("""{"queuePosition":1}"""), cancel);
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
    /// downloadUrl, so that one shape of URL covers a container artifact and a pipeline artifact
    /// alike.
    /// <para>
    /// It does not avoid the artifacts host: Azure DevOps answers this with a redirect to the same
    /// place the downloadUrl names, and that host wants the personal access token too. Nothing here
    /// makes that work, <see cref="RedirectingHandler"/> does, and without it every download landed
    /// anonymous and was answered with a sign in page.
    /// </para>
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
