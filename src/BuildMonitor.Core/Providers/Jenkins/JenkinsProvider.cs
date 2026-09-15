/// <summary>
/// https://www.jenkins.io/doc/book/using/remote-access-api/
/// <para>
/// Jobs nest in folders and multibranch projects, so discovery walks the tree; a branch of a
/// multibranch project is a pipeline of its own, named for the branch. Jenkins has no rerun:
/// a retry queues a new build of the job.
/// </para>
/// </summary>
sealed class JenkinsProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.Jenkins;

    const string jobFields = "name,displayName,url,color,_class";
    const string buildFields = "number,url,result,building,timestamp,duration,estimatedDuration,actions[lastBuiltRevision[branch[name]]]";

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var root = await context.Http.Get($"api/json?tree=jobs[{jobFields},jobs[{jobFields},jobs[{jobFields}]]]", JenkinsContext.Default.JenkinsNode, cancel);
        var pipelines = new List<Pipeline>();
        await Walk(context, root, [], null, pipelines, cancel);
        return pipelines;
    }

    static async Task Walk(ProviderContext context, JenkinsNode node, List<string> path, JenkinsNode? multibranch, List<Pipeline> pipelines, Cancel cancel)
    {
        foreach (var job in node.Jobs ?? [])
        {
            var name = job.DisplayName ?? job.Name;
            var container = IsContainer(job.Class);
            if (container)
            {
                if (job.Jobs is null)
                {
                    // Deeper than the one request reached; ask the folder itself.
                    var fetched = await context.Http.Get($"{job.Url}api/json?tree=jobs[{jobFields},jobs[{jobFields},jobs[{jobFields}]]]", JenkinsContext.Default.JenkinsNode, cancel);
                    job.Jobs = fetched.Jobs;
                }

                await Walk(context, job, [..path, name], IsMultibranch(job.Class) ? job : null, pipelines, cancel);
                continue;
            }

            var full = string.Join(" / ", path.Append(name));
            pipelines.Add(new(
                job.Url,
                full,
                multibranch is null ? full : string.Join(" / ", path),
                multibranch is null ? null : name,
                job.Url));
        }
    }

    static bool IsContainer(string? jenkinsClass) =>
        jenkinsClass is not null &&
        (jenkinsClass.Contains("Folder", StringComparison.Ordinal) ||
         jenkinsClass.Contains("MultiBranchProject", StringComparison.Ordinal));

    static bool IsMultibranch(string? jenkinsClass) =>
        jenkinsClass is not null &&
        jenkinsClass.Contains("MultiBranchProject", StringComparison.Ordinal);

    const string probeFields = "url,_class,nextBuildNumber,inQueue";

    /// <summary>
    /// The job tree again, as deep as discovery's first request, with each job's next build
    /// number and whether a build is queued as its token; neither reads a build record. Jenkins
    /// sends no ETags and fetching a job reads its last builds from disk, so without this every
    /// quiet job cost that each time its schedule came round. Jobs deeper than the tree reaches
    /// have no token and wait for the schedule.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        var root = await context.Http.Get($"api/json?tree=jobs[{probeFields},jobs[{probeFields},jobs[{probeFields}]]]", JenkinsContext.Default.JenkinsNode, cancel);
        var tokens = ImmutableDictionary.CreateBuilder<string, string>();
        Collect(root, tokens);
        return tokens.ToImmutable();
    }

    static void Collect(JenkinsNode node, ImmutableDictionary<string, string>.Builder tokens)
    {
        foreach (var job in node.Jobs ?? [])
        {
            if (IsContainer(job.Class))
            {
                Collect(job, tokens);
            }
            else if (job.NextBuildNumber is { } next)
            {
                tokens[job.Url] = $"{next}|{job.InQueue}";
            }
        }
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        foreach (var pipeline in pipelines)
        {
            var job = await context.Http.Get($"{pipeline.Url}api/json?tree=builds[{buildFields}]{{0,{perPipeline}}},inQueue,queueItem[id,inQueueSince]", JenkinsContext.Default.JenkinsJob, cancel);
            if (job.InQueue &&
                job.QueueItem is { } queued)
            {
                var since = queued.InQueueSince is { } millis ? DateTimeOffset.FromUnixTimeMilliseconds(millis) : (DateTimeOffset?) null;
                builds.Add(new(
                    context.Connection.Id,
                    pipeline.Id,
                    pipeline.Name,
                    pipeline.RepoName,
                    Branch(pipeline, null),
                    "",
                    BuildStatus.Queued,
                    "queued",
                    since,
                    null,
                    null,
                    null,
                    pipeline.Url,
                    null,
                    PullRequest(pipeline),
                    null,
                    null,
                    null,
                    null,
                    CanRetry: false,
                    CanCancel: true,
                    Join(pipeline.Url, $"queue:{queued.Id}"),
                    pipeline.Url));
            }

            builds.AddRange(job.Builds.Select(_ => Convert(context.Connection.Id, pipeline, _)));
        }

        return builds;
    }

    static Build Convert(string connectionId, Pipeline pipeline, JenkinsBuild build)
    {
        var status = build.Building
            ? BuildStatus.Running
            : build.Result switch
            {
                "SUCCESS" => BuildStatus.Succeeded,
                "FAILURE" or "UNSTABLE" => BuildStatus.Failed,
                "ABORTED" => BuildStatus.Cancelled,
                null => BuildStatus.Queued,
                _ => BuildStatus.Unknown
            };
        var started = build.Timestamp is null ? (DateTimeOffset?) null : DateTimeOffset.FromUnixTimeMilliseconds(build.Timestamp.Value);
        var finished = build.Building || started is null || build.Duration is null
            ? null
            : started + TimeSpan.FromMilliseconds(build.Duration.Value);
        var revision = build.Actions?
            .Select(_ => _.LastBuiltRevision?.Branch?.FirstOrDefault()?.Name)
            .FirstOrDefault(_ => _ is not null);
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            Branch(pipeline, revision),
            build.Number.ToString(),
            status,
            build.Building ? "building" : build.Result?.ToLowerInvariant(),
            started,
            started,
            finished,
            build.EstimatedDuration is > 0 ? new(TimeSpan.FromMilliseconds(build.EstimatedDuration.Value), null, null) : null,
            build.Url,
            null,
            PullRequest(pipeline),
            null,
            null,
            null,
            null,
            CanRetry: !build.Building,
            CanCancel: build.Building,
            Join(pipeline.Url, build.Number.ToString()),
            pipeline.Url);
    }

    /// <summary>
    /// A multibranch job knows its branch by name; anything else only knows what the git plugin
    /// recorded, which arrives as a remote ref.
    /// </summary>
    static string? Branch(Pipeline pipeline, string? revision)
    {
        if (pipeline.Group is not null)
        {
            return pipeline.Group;
        }

        if (revision is null)
        {
            return null;
        }

        foreach (var prefix in (string[]) ["refs/remotes/origin/", "refs/heads/", "origin/"])
        {
            if (revision.StartsWith(prefix, StringComparison.Ordinal))
            {
                return revision[prefix.Length..];
            }
        }

        return revision;
    }

    /// <summary>
    /// The branch source plugins name a pull request's branch job PR-n.
    /// </summary>
    static string? PullRequest(Pipeline pipeline) =>
        pipeline.Group is { } branch &&
        branch.StartsWith("PR-", StringComparison.Ordinal) &&
        int.TryParse(branch.AsSpan(3), out var number)
            ? number.ToString()
            : null;

    public override async Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var jobUrl = Split(build)[0];
        var crumb = await Crumb(context, cancel);
        var status = await Post(context, $"{jobUrl}build", crumb, cancel);
        if (status == HttpStatusCode.BadRequest)
        {
            // A parameterized job refuses a plain build; buildWithParameters takes the defaults.
            status = await Post(context, $"{jobUrl}buildWithParameters", crumb, cancel);
        }

        if (status is not (HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Found))
        {
            throw new HttpRequestException($"{(int) status} from {jobUrl}build");
        }
    }

    public override async Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var crumb = await Crumb(context, cancel);
        var path = parts[1].StartsWith("queue:", StringComparison.Ordinal)
            ? $"queue/cancelItem?id={parts[1][6..]}"
            : $"{parts[0]}{parts[1]}/stop";
        var status = await Post(context, path, crumb, cancel);
        if (status is not (HttpStatusCode.OK or HttpStatusCode.Found or HttpStatusCode.NoContent))
        {
            throw new HttpRequestException($"{(int) status} from {path}");
        }
    }

    /// <summary>
    /// The build's console, whole: it is the one log a Jenkins build keeps, and a Pipeline's stages
    /// are only markers inside it.
    /// </summary>
    public override Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.GetLog($"{parts[0]}{parts[1]}/consoleText", cancel);
    }

    static Task<HttpStatusCode> Post(ProviderContext context, string path, JenkinsCrumb? crumb, Cancel cancel)
    {
        var content = new StringContent("");
        if (crumb is null)
        {
            return context.Http.TrySend(HttpMethod.Post, path, content, cancel);
        }

        return context.Http.TrySend(HttpMethod.Post, path, content, cancel, [new(crumb.CrumbRequestField, crumb.Crumb)]);
    }

    /// <summary>
    /// CSRF protection wants a crumb on every POST from a session; an API token does not need
    /// one, but sending it when the issuer exists costs nothing and keeps older setups working.
    /// </summary>
    static async Task<JenkinsCrumb?> Crumb(ProviderContext context, Cancel cancel)
    {
        try
        {
            return await context.Http.Get("crumbIssuer/api/json", JenkinsContext.Default.JenkinsCrumb, cancel);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("me/api/json?tree=fullName,id", JenkinsContext.Default.JenkinsUser, cancel);
        return new(true, $"Signed in as {user.FullName ?? user.Id}");
    }
}
