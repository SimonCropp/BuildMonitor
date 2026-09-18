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
            foreach (var type in response.BuildType)
            {
                if (!byType.TryGetValue(type.Id, out var pipeline))
                {
                    continue;
                }

                builds.AddRange(
                    (type.Builds?.Build ?? [])
                    .Where(_ => !RemovedFromQueue(_))
                    .Take(perPipeline)
                    .Select(_ => Convert(context, pipeline, _)));
            }
        }

        return builds;
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
    /// The build log, from the download beside the REST API, which has no call for it. The download
    /// takes the same access token.
    /// </summary>
    public override Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel) =>
        context.Http.GetLog($"../../downloadBuildLog.html?buildId={Split(build)[1]}", cancel);

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var server = await context.Http.Get("server", TeamCityContext.Default.TeamCityServer, cancel);
        return new(true, $"TeamCity {server.Version}");
    }
}
