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

    const string jobFields = "name,displayName,url,_class";
    const string buildFields = "number,url,result,building,timestamp,duration";
    // The parameters a build was started with, one of which may say who the run is for.
    const string parameterFields = $"parameters[name,value]{{0,{parameterLimit}}}";
    // What the git plugin recorded, the only branch a job outside a multibranch project has. Both
    // live in the build's actions, asked for as one selector rather than two of the same name.
    const string revisionFields = "lastBuiltRevision[branch[name]]";

    /// <summary>
    /// How many of a build's parameters are read, which is only ever for the one this looks for.
    /// A job may declare dozens, and every one of them for every build of every job would be most
    /// of the response, where a tree cannot ask for a parameter by name.
    /// </summary>
    const string parameterLimit = "25";

    /// <summary>
    /// How many levels of folders one request reads. A level no folder reaches costs nothing, where
    /// each folder deeper than three levels cost a request of its own, one after another.
    /// </summary>
    const int treeDepth = 6;

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var root = await context.Http.Get($"api/json?tree={Tree(jobFields, treeDepth)}", JenkinsContext.Default.JenkinsNode, cancel);
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
                // Deeper than the one request reached; ask the folder itself.
                var children = job.Jobs is null
                    ? await context.Http.Get($"{job.Url}api/json?tree={Tree(jobFields, treeDepth)}", JenkinsContext.Default.JenkinsNode, cancel)
                    : job;
                await Walk(context, children, [..path, name], IsMultibranch(job.Class) ? job : null, pipelines, cancel);
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

    /// <summary>
    /// jobs[fields,jobs[fields,…]], <paramref name="depth"/> levels deep.
    /// </summary>
    static string Tree(string fields, int depth)
    {
        var tree = $"jobs[{fields}]";
        for (var level = 1; level < depth; level++)
        {
            tree = $"jobs[{fields},{tree}]";
        }

        return tree;
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
    /// The job tree again, with each job's next build number and whether a build is queued as its
    /// token; neither reads a build record. Jenkins sends no ETags and fetching a job reads its last
    /// builds from disk, so without this every quiet job cost that each time its schedule came round.
    /// As deep as the deepest job discovered, which discovery may have reached through a folder of
    /// its own: a job below the tree had no token and waited for its schedule.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        var depth = groups
            .SelectMany(_ => _.Pipelines)
            .Select(_ => Depth(_.Url))
            .Append(treeDepth)
            .Max();
        var root = await context.Http.Get($"api/json?tree={Tree(probeFields, depth)}", JenkinsContext.Default.JenkinsNode, cancel);
        var tokens = ImmutableDictionary.CreateBuilder<string, string>();
        Collect(root, tokens);
        return tokens.ToImmutable();
    }

    /// <summary>
    /// How many folders deep a job is, from its URL, where each level adds a job/ segment.
    /// </summary>
    static int Depth(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return 0;
        }

        return uri.Segments.Count(_ => _ == "job/");
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
            // A branch of a multibranch project is named for its branch, so it does not ask what the
            // git plugin recorded.
            var actions = pipeline.Group is null ? $"actions[{revisionFields},{parameterFields}]" : $"actions[{parameterFields}]";
            var fields = $"{buildFields},{actions}";
            var job = await context.Http.Get($"{pipeline.Url}api/json?tree=builds[{fields}]{{0,{perPipeline}}},lastBuild[number,estimatedDuration],inQueue,queueItem[id,inQueueSince]", JenkinsContext.Default.JenkinsJob, cancel);
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

            builds.AddRange(job.Builds.Select(_ => Convert(context, pipeline, _, Estimate(job, _))));
        }

        return builds;
    }

    /// <summary>
    /// Jenkins' estimate, for the job's last build alone, which is the one that can be running.
    /// Asked of every build, each walked up to six earlier builds for an estimate nothing read.
    /// </summary>
    static ProviderEstimate? Estimate(JenkinsJob job, JenkinsBuild build)
    {
        if (job.LastBuild is not { EstimatedDuration: long milliseconds and > 0 } last ||
            last.Number != build.Number)
        {
            return null;
        }

        return new(TimeSpan.FromMilliseconds(milliseconds), null, null);
    }

    /// <summary>
    /// Who the row names: whoever <see cref="TriggeredBy"/> says the build is for. Jenkins names
    /// nobody itself, so a build without one names no one, as every Jenkins row did before.
    /// </summary>
    static string? Author(ProviderContext context, JenkinsBuild build) =>
        TriggeredBy.Author(context, build.Parameter(TriggeredBy.Property), $"Build {build.Number}");

    static Build Convert(ProviderContext context, Pipeline pipeline, JenkinsBuild build, ProviderEstimate? estimate)
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
            context.Connection.Id,
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
            estimate,
            build.Url,
            null,
            PullRequest(pipeline),
            null,
            null,
            null,
            Author(context, build),
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

    /// <summary>
    /// What the build archived, each file on its own rather than the build's <c>archive.zip</c>,
    /// which is every artifact in one download. With no size to weigh, one blob that could be
    /// anything from a kilobyte to a gigabyte is the single thing a budget cannot plan around.
    /// <para>
    /// Jenkins reports no artifact size in its API, so each is listed without one and held to the
    /// per file cap while it copies instead.
    /// </para>
    /// </summary>
    public override async Task<IReadOnlyList<BuildArtifact>> ListArtifacts(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var listed = await context.Http.Get(
            $"{parts[0]}{parts[1]}/api/json?tree=artifacts[fileName,relativePath]",
            JenkinsContext.Default.JenkinsArtifacts,
            cancel);
        return listed.Artifacts
            // The relative path rather than the file name: two modules archiving a results file
            // each call it the same thing, and only the path says which module it came from.
            .Select(_ => new BuildArtifact(_.RelativePath, _.RelativePath, null))
            .ToList();
    }

    public override Task<long> DownloadArtifact(ProviderContext context, Build build, BuildArtifact artifact, Stream destination, long maxBytes, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Download($"{parts[0]}{parts[1]}/artifact/{EncodePath(artifact.Id)}", destination, maxBytes, cancel);
    }

    static Task<HttpStatusCode> Post(ProviderContext context, string path, JenkinsCrumb? crumb, Cancel cancel)
    {
        List<KeyValuePair<string, string>> headers = [];
        if (crumb is not null)
        {
            headers.Add(new(crumb.CrumbRequestField, crumb.Crumb));
        }

        headers.Add(Referer(context));
        return context.Http.TrySend(HttpMethod.Post, path, new StringContent(""), cancel, headers);
    }

    /// <summary>
    /// Where Jenkins sends the client after an action. A stop, and a queue cancel on older
    /// versions, answer with a redirect to the page named in Referer, or to the build when there
    /// is none. The handler follows a redirect without the Authorization header. So a server that
    /// anonymous users may not read refused the build page with a 403, and Cancel reported a
    /// refusal for a build that had stopped. Anyone may read whoAmI, so the redirect lands on a
    /// page that answers.
    /// </summary>
    static KeyValuePair<string, string> Referer(ProviderContext context) =>
        new("Referer", new Uri(context.Http.BaseAddress, "whoAmI/api/json").ToString());

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
