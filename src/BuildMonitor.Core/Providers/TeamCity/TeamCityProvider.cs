/// <summary>
/// https://www.jetbrains.com/help/teamcity/rest/teamcity-rest-api-documentation.html
/// <para>
/// One request fetches the latest builds of every configuration in a project, rather than one
/// request per configuration: a server with a few hundred build configurations would otherwise cost
/// a few hundred calls a poll. The builds are asked for per configuration inside that request. A
/// flat list of recent builds puts queued builds first, so a long queue filled it and hid every
/// configuration that had not built lately. A project at a time rather than the whole server, which
/// while anything on it ran was about a megabyte every ten to thirty seconds on a server of five
/// hundred configurations.
/// </para>
/// </summary>
sealed class TeamCityProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.TeamCity;

    // The one parameter is asked for by name: a build's parameters run to dozens, and all of them
    // for every build of every configuration would be most of the response. A credential that may
    // not read parameters is answered without the collection at all, and the override is then
    // simply not there.
    const string propertyFields = $"properties($locator(name:{TriggeredBy.Property}),property(name,value))";

    const string buildFields = $"build(id,number,status,state,branchName,defaultBranch,webUrl,statusText,queuedDate,startDate,finishDate,buildTypeId,canceledInfo(text),running-info(percentageComplete,elapsedSeconds,estimatedTotalSeconds,leftSeconds),triggered(user(username,name)),revisions(revision(version)),{propertyFields})";

    // The newest build id the probe has seen, which the next probe asks for the builds after.
    const string newestBuild = "teamcity.newest-build";

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
        var builds = new List<Build>();
        // The group is the project's id, as its name need not be unique.
        foreach (var project in pipelines.GroupBy(_ => _.Group))
        {
            var byType = project.ToDictionary(_ => _.Id);
            var locator = project.Key is null
                ? $"affectedProject:(id:{Encode(Project(context))})"
                : $"project:(id:{Encode(project.Key)})";
            // No queuedDate for the history limit: a build queued before the cutoff and still queued or
            // running would never be returned. count bounds the response instead.
            var response = await context.Http.Get(
                $"buildTypes?locator={locator}&fields=buildType(id,builds($locator(branch:default:any,state:any,canceled:any,failedToStart:any,count:{perPipeline}),{buildFields}))",
                TeamCityContext.Default.TeamCityBuildTypes,
                cancel);
            var missing = new List<Pipeline>();
            foreach (var type in response.BuildType)
            {
                if (!byType.TryGetValue(type.Id, out var pipeline))
                {
                    continue;
                }

                var kept = (type.Builds?.Build ?? [])
                    .Where(_ => !RemovedFromQueue(_))
                    .Take(perPipeline)
                    .ToList();
                var defaultBranch = DefaultBranch(context, pipeline, kept);
                builds.AddRange(kept.Select(_ => Convert(context, pipeline, _) with { DefaultBranch = defaultBranch }));
                if (MissesDefaultRun(context, pipeline, kept, defaultBranch))
                {
                    missing.Add(pipeline);
                }
            }

            if (missing.Count > 0)
            {
                builds.AddRange(await DefaultRuns(context, missing, cancel));
            }
        }

        return builds;
    }

    /// <summary>
    /// What the none memory files a configuration's default branch under before a build has named
    /// it.
    /// </summary>
    const string unnamedDefault = "<default>";

    /// <summary>
    /// The branch the configuration's own builds are on: TeamCity flags each build on the default
    /// branch, so it is the branch of any flagged build, remembered for the windows with none.
    /// </summary>
    static string? DefaultBranch(ProviderContext context, Pipeline pipeline, List<TeamCityBuild> builds)
    {
        var key = $"default-branch|{pipeline.Id}";
        if (builds.FirstOrDefault(_ => _.DefaultBranch == true)?.BranchName is { } flagged)
        {
            context.Memory.Set(key, flagged);
            return flagged;
        }

        context.Memory.TryGet<string>(key, out var remembered);
        return remembered;
    }

    /// <summary>
    /// Whether a configuration that builds branches has no build on its default branch in its
    /// window, as a burst of pull requests leaves it, and was not asked for one lately and found
    /// none. A configuration that builds no branches names none on any build, and every build of it
    /// is already its own.
    /// </summary>
    static bool MissesDefaultRun(ProviderContext context, Pipeline pipeline, List<TeamCityBuild> builds, string? defaultBranch) =>
        builds.Any(_ => _.BranchName is not null) &&
        builds.All(_ => _.DefaultBranch != true) &&
        !DefaultRunMemory.KnownNone(context.Memory, pipeline.Id, defaultBranch ?? unnamedDefault, DateTimeOffset.UtcNow);

    /// <summary>
    /// The newest build on the default branch of each configuration missing one, in one request
    /// for them all, by the locator TeamCity has for that branch whatever it is called. A few
    /// rather than one, as a build removed from the queue is listed and is not a run. One with
    /// none since the history cutoff is not asked again for an hour.
    /// </summary>
    static async Task<List<Build>> DefaultRuns(ProviderContext context, List<Pipeline> missing, Cancel cancel)
    {
        var items = string.Join(',', missing.Select(_ => $"item:(id:{Encode(_.Id)})"));
        var response = await GetOrNone(
            context,
            $"buildTypes?locator={items}&fields=buildType(id,builds($locator(branch:(default:true),state:any,canceled:any,failedToStart:any,count:3),{buildFields}))",
            TeamCityContext.Default.TeamCityBuildTypes,
            cancel);
        var now = DateTimeOffset.UtcNow;
        var found = new List<Build>();
        foreach (var pipeline in missing)
        {
            context.Memory.TryGet<string>($"default-branch|{pipeline.Id}", out var remembered);
            var run = response?.BuildType
                .FirstOrDefault(_ => _.Id == pipeline.Id)?
                .Builds?.Build
                .FirstOrDefault(_ => !RemovedFromQueue(_) && _.DefaultBranch == true);
            if (run?.BranchName is not { } branch)
            {
                DefaultRunMemory.None(context.Memory, pipeline.Id, remembered ?? unnamedDefault, now);
                continue;
            }

            var build = Convert(context, pipeline, run) with
            {
                DefaultBranch = branch
            };
            if (context.Since is { } since &&
                !HistoryCutoff.Keeps(build, since))
            {
                DefaultRunMemory.None(context.Memory, pipeline.Id, branch, now);
                continue;
            }

            context.Memory.Set($"default-branch|{pipeline.Id}", branch);
            DefaultRunMemory.Found(context.Memory, pipeline.Id, branch);
            found.Add(build);
        }

        return found;
    }

    /// <summary>
    /// The builds queued since the newest one seen, anywhere under the project, with the newest
    /// build of each project as its token. TeamCity sends no ETags, so without this a quiet project
    /// was fetched whole each time its schedule came round. The first probe asks for the single
    /// newest build, to have an id to ask after. A change to an older build, such as a running one
    /// finishing, is not in the answer, and waits for the schedule.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        var projectOf = new Dictionary<string, string>();
        foreach (var group in groups)
        {
            foreach (var pipeline in group.Pipelines)
            {
                projectOf[pipeline.Id] = group.Key;
            }
        }

        var filter = context.Memory.TryGet<long>(newestBuild, out var newest)
            ? $"sinceBuild:(id:{newest}),count:1000"
            : "count:1";
        var response = await context.Http.Get(
            $"builds?locator=affectedProject:(id:{Encode(Project(context))}),branch:default:any,state:any,canceled:any,failedToStart:any,{filter}&fields=build(id,buildTypeId)",
            TeamCityContext.Default.TeamCityBuilds,
            cancel);
        var tokens = previous.ToBuilder();
        foreach (var build in response.Build)
        {
            newest = Math.Max(newest, build.Id);
            if (build.BuildTypeId is null ||
                !projectOf.TryGetValue(build.BuildTypeId, out var project))
            {
                continue;
            }

            if (!tokens.TryGetValue(project, out var token) ||
                build.Id > long.Parse(token, CultureInfo.InvariantCulture))
            {
                tokens[project] = build.Id.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (response.Build.Count > 0)
        {
            context.Memory.Set(newestBuild, newest);
        }

        return tokens.ToImmutable();
    }

    /// <summary>
    /// Who the row names: whoever <see cref="TriggeredBy"/> says the build is for, then whoever
    /// triggered it.
    /// </summary>
    static string? Author(ProviderContext context, TeamCityBuild build) =>
        TriggeredBy.Author(context, build.Property(TriggeredBy.Property), $"Build {build.Id}") ??
        build.Triggered?.User?.Name ??
        build.Triggered?.User?.Username;

    static Build Convert(ProviderContext context, Pipeline pipeline, TeamCityBuild build)
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
            context.Connection.Id,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            build.BranchName,
            RunNumber(build),
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
            Author(context, build),
            CanRetry: build.State == "finished",
            CanCancel: build.State is "queued" or "running",
            Join(build.State, build.Id.ToString(), build.BuildTypeId, build.BranchName),
            pipeline.Url);
    }

    /// <summary>
    /// A build taken off the queue before it started. TeamCity keeps it as a canceled build that was
    /// never numbered: its number is N/A. Its start and finish dates are both the moment it was
    /// removed, so they do not tell it apart. Listed, it took the place of the configuration's last
    /// real run on the row. Cancelling a queued build turned the row cancelled, whatever had run
    /// before, where a cancelled Jenkins queue item leaves its row as it was.
    /// </summary>
    static bool RemovedFromQueue(TeamCityBuild build) =>
        build is
        {
            State: "finished",
            CanceledInfo: not null,
            Number: null or "N/A"
        };

    /// <summary>
    /// TeamCity numbers a build when it starts. A queued build has no number yet, and one that
    /// never started is numbered N/A. The build id stood in for a missing number, but ids and
    /// numbers count separately, so a queued row read #4 and then #3 once the build started.
    /// </summary>
    static string RunNumber(TeamCityBuild build)
    {
        if (build.Number is null or "N/A")
        {
            return "";
        }

        return build.Number;
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

    /// <summary>
    /// Moves the queued build to the front of the queue. The position is a word rather than a
    /// number: the call takes "first", "last" or "1" and refuses anything else, so there is no
    /// arithmetic here that a queue changing underneath could make wrong.
    /// </summary>
    public override Task RunNext(ProviderContext context, Build build, Cancel cancel)
    {
        var body = new TeamCityBuildReference(long.Parse(Split(build)[1]));
        return context.Http.Send(HttpMethod.Put, "buildQueue/order/first", HttpJson.Json(body, TeamCityContext.Default.TeamCityBuildReference), cancel);
    }

    /// <summary>
    /// The build log, from the download beside the REST API, which has no call for it. The download
    /// takes the same access token.
    /// </summary>
    public override Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel) =>
        context.Http.GetLog($"../../downloadBuildLog.html?buildId={Split(build)[1]}", cancel);

    /// <summary>
    /// How far into an artifact tree to look. A build that publishes into folders would otherwise
    /// be listed as holding nothing, and a deep tree would cost a request per folder for files a
    /// budget would not reach anyway.
    /// </summary>
    const int maxArtifactDepth = 2;

    /// <summary>
    /// The build's artifacts, walked a folder at a time. The listing is one level deep per call and
    /// names the link to each folder's own level, so a tree is walked rather than asked for at once.
    /// </summary>
    public override async Task<IReadOnlyList<BuildArtifact>> ListArtifacts(ProviderContext context, Build build, Cancel cancel)
    {
        var artifacts = new List<BuildArtifact>();
        await Walk(context, $"builds/id:{Split(build)[1]}/artifacts/children", "", 0, artifacts, cancel);
        return artifacts;
    }

    static async Task Walk(ProviderContext context, string path, string prefix, int depth, List<BuildArtifact> artifacts, Cancel cancel)
    {
        var listed = await context.Http.Get(
            $"{path}?fields=file(name,size,content(href),children(href))",
            TeamCityContext.Default.TeamCityArtifacts,
            cancel);
        foreach (var entry in listed.File)
        {
            var name = prefix.Length == 0 ? entry.Name : $"{prefix}/{entry.Name}";
            if (entry.Content is { Href.Length: > 0 } content)
            {
                artifacts.Add(new(content.Href, name, entry.Size));
                continue;
            }

            if (entry.Children is { Href.Length: > 0 } children &&
                depth < maxArtifactDepth)
            {
                await Walk(context, children.Href, name, depth + 1, artifacts, cancel);
            }
        }
    }

    public override Task<long> DownloadArtifact(ProviderContext context, Build build, BuildArtifact artifact, Stream destination, long maxBytes, Cancel cancel) =>
        context.Http.Download(artifact.Id, destination, maxBytes, cancel);

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var server = await context.Http.Get("server", TeamCityContext.Default.TeamCityServer, cancel);
        return new(true, $"TeamCity {server.Version}");
    }
}
