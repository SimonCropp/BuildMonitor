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
        if (account.Length == 0)
        {
            return "api";
        }

        return $"api/account/{Encode(account)}";
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
                $"https://ci.appveyor.com/project/{_.AccountName}/{_.Slug}",
                RepoUrl(_),
                DefaultBranch: _.RepositoryBranch))
            .ToList();
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        foreach (var pipeline in pipelines)
        {
            // A project deleted since discovery answers with a 404, which would fail the whole
            // fetch; it has no builds until the next discovery drops it.
            var history = await GetOrNone(
                context,
                $"api/projects/{pipeline.Id}/history?recordsNumber={perPipeline}",
                AppVeyorContext.Default.AppVeyorHistory,
                cancel);
            if (history is null)
            {
                continue;
            }

            var runs = history.Builds;
            var defaultBranch = DefaultBranch(context, pipeline, runs);
            if (defaultBranch is not null &&
                await DefaultRun(context, pipeline, runs, defaultBranch, cancel) is { } run)
            {
                runs = [.. runs, run];
            }

            builds.AddRange(runs.Select(_ => Convert(context.Connection.Id, pipeline, _) with { DefaultBranch = defaultBranch }));
        }

        return builds;
    }

    /// <summary>
    /// The branch the project's own builds are on. AppVeyor's setting for it is what the repository's
    /// default branch was when the project was added: VerifyTests projects that moved to main still
    /// say master. So the history is the witness: the branch its pull request builds target, which is
    /// what a pull request build's branch is, or any other build ran on, remembered from the last
    /// history that had pull requests in it for the histories that have none. The setting is passed
    /// over for an hour once it has been asked for its newest build and had none.
    /// </summary>
    static string? DefaultBranch(ProviderContext context, Pipeline pipeline, List<AppVeyorBuild> runs)
    {
        var memory = context.Memory;
        var key = $"default-branch|{pipeline.Id}";
        memory.TryGet<string>(key, out var remembered);
        var configured = pipeline.DefaultBranch;
        if (configured is not null &&
            DefaultRunMemory.KnownNone(memory, pipeline.Id, configured, DateTimeOffset.UtcNow))
        {
            configured = null;
        }

        var targets = runs
            .Where(_ => _.PullRequestId is not null)
            .Select(_ => _.Branch)
            .OfType<string>()
            .ToList();
        var built = runs
            .Where(_ => _.PullRequestId is null)
            .Select(_ => _.Branch)
            .OfType<string>()
            .ToHashSet();
        var chosen = DefaultBranches.Choose([configured, remembered], targets, built);
        if (targets.Count > 0 &&
            chosen is not null)
        {
            memory.Set(key, chosen);
        }

        return chosen;
    }

    /// <summary>
    /// The newest build pushed to the default branch, where the history left it out. The history is
    /// the last few builds of any kind, and a batch of pull requests fills it: each one builds its
    /// branch and then the pull request, so six pull requests pushed Verify.EntityFramework's last
    /// build of main out of the five, and the pipeline had no main to show. The branch route answers
    /// with the newest build that is not a pull request's, or a 404 where there is none.
    /// </summary>
    static async Task<AppVeyorBuild?> DefaultRun(ProviderContext context, Pipeline pipeline, List<AppVeyorBuild> runs, string branch, Cancel cancel)
    {
        var memory = context.Memory;
        if (runs.Any(_ => _.PullRequestId is null && _.Branch == branch))
        {
            DefaultRunMemory.Found(memory, pipeline.Id, branch);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (DefaultRunMemory.KnownNone(memory, pipeline.Id, branch, now))
        {
            return null;
        }

        var detail = await GetOrNone(context, $"api/projects/{pipeline.Id}/branch/{Encode(branch)}", AppVeyorContext.Default.AppVeyorBuildDetail, cancel);
        if (detail?.Build is not { } run ||
            (context.Since is { } since && !HistoryCutoff.Keeps(Convert(context.Connection.Id, pipeline, run), since)))
        {
            DefaultRunMemory.None(memory, pipeline.Id, branch, now);
            return null;
        }

        DefaultRunMemory.Found(memory, pipeline.Id, branch);
        return run;
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

    /// <summary>
    /// The repository behind the project, for the projects whose address AppVeyor gives enough of
    /// to compose one. It names the type and the "owner/name" pair, but not the host, so only
    /// GitHub's is safe to build; the rest leave the row's name plain rather than guess a host.
    /// </summary>
    static string? RepoUrl(AppVeyorProject project)
    {
        if (project.RepositoryName is { } name &&
            string.Equals(project.RepositoryType, "gitHub", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://github.com/{name}";
        }

        return null;
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
        var repo = pipeline.RepoUrl;
        var (branch, branchUrl) = BranchOf(pipeline, build);
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            branch,
            build.BuildNumber.ToString(),
            status,
            build.Status,
            build.Created,
            build.Started,
            build.Finished,
            null,
            $"{pipeline.Url}/builds/{build.BuildId}",
            branchUrl,
            build.PullRequestId,
            repo is not null && build.PullRequestId is not null ? $"{repo}/pull/{build.PullRequestId}" : null,
            build.CommitId,
            build.Message,
            build.AuthorName,
            CanRetry: status is BuildStatus.Failed or BuildStatus.Cancelled or BuildStatus.Succeeded,
            CanCancel: status is BuildStatus.Queued or BuildStatus.Running,
            Join(build.BuildId.ToString(), build.Version),
            pipeline.Url,
            pipeline.RepoUrl);
    }

    /// <summary>
    /// The branch a build ran on, and its page where the project is on GitHub. A pull request
    /// build's branch field is the branch it targets, which filed every pull request's build as a
    /// build of main, so its own comes from the head fields, behind the fork's owner where it came
    /// from one, and its page is in the repository it lives in.
    /// </summary>
    static (string? Branch, string? Url) BranchOf(Pipeline pipeline, AppVeyorBuild build)
    {
        var repo = pipeline.RepoUrl;
        if (build.PullRequestId is not { } number)
        {
            if (repo is null ||
                build.Branch is null)
            {
                return (build.Branch, null);
            }

            return (build.Branch, $"{repo}/tree/{build.Branch}");
        }

        if (build.PullRequestHeadBranch is not { Length: > 0 } head)
        {
            return (PullRequestBranches.Unnamed(number), null);
        }

        var branch = PullRequestBranches.Head(head, PullRequestBranches.ForkOwner(build.PullRequestHeadRepository, pipeline.RepoName));
        if (repo is null)
        {
            return (branch, null);
        }

        var headRepo = build.PullRequestHeadRepository is { Length: > 0 } headRepository ? $"https://github.com/{headRepository}" : repo;
        return (branch, $"{headRepo}/tree/{head}");
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

    /// <summary>
    /// The logs of the build's failed jobs. The build is read by the route that names the account
    /// in its own path, as history is, and a job's log by the job's id, so neither takes the
    /// prefix; a route that wanted it would answer with the web app, which the log fetch refuses
    /// rather than copying. A job is often unnamed, and then goes by its id.
    /// </summary>
    public override async Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel)
    {
        var version = Split(build)[1];
        var detail = await context.Http.Get($"api/projects/{build.PipelineId}/build/{Encode(version)}", AppVeyorContext.Default.AppVeyorBuildDetail, cancel);
        var logs = new List<(string Name, string Log)>();
        foreach (var job in detail.Build?.Jobs.Where(_ => _.Status == "failed") ?? [])
        {
            var name = string.IsNullOrEmpty(job.Name) ? job.JobId : job.Name;
            logs.Add((name, await context.Http.GetLog($"api/buildjobs/{job.JobId}/log", cancel)));
        }

        return Sections(logs);
    }

    /// <summary>
    /// How many of a build's jobs are asked for their artifacts. A wide matrix would otherwise cost
    /// a request per leg for files the budget would never reach.
    /// </summary>
    const int maxArtifactJobs = 10;

    /// <summary>
    /// Every job's artifacts, not only the failed ones. In a matrix the leg that broke often
    /// published nothing while a sibling holds the test report that says why, so listing only the
    /// failures would hide the evidence.
    /// <para>
    /// Both routes go without the account prefix, as the log's do: a prefixed route answers with
    /// the web app's HTML, which a download refuses rather than saving as a file.
    /// </para>
    /// </summary>
    public override async Task<IReadOnlyList<BuildArtifact>> ListArtifacts(ProviderContext context, Build build, Cancel cancel)
    {
        var version = Split(build)[1];
        var detail = await context.Http.Get($"api/projects/{build.PipelineId}/build/{Encode(version)}", AppVeyorContext.Default.AppVeyorBuildDetail, cancel);
        var artifacts = new List<BuildArtifact>();
        foreach (var job in (detail.Build?.Jobs ?? []).Take(maxArtifactJobs))
        {
            var listed = await context.Http.Get($"api/buildjobs/{job.JobId}/artifacts", AppVeyorContext.Default.ListAppVeyorArtifact, cancel);
            // The job id travels with the file name, because a download is addressed by both and a
            // build's artifacts come from several jobs.
            artifacts.AddRange(listed.Select(_ => new BuildArtifact($"{job.JobId}|{_.FileName}", _.FileName, _.Size)));
        }

        return artifacts;
    }

    public override Task<long> DownloadArtifact(ProviderContext context, Build build, BuildArtifact artifact, Stream destination, long maxBytes, Cancel cancel)
    {
        // The first separator only: a published file's name may hold one, and it belongs to the
        // name rather than to the pair.
        var separator = artifact.Id.IndexOf('|');
        var job = artifact.Id[..separator];
        var fileName = artifact.Id[(separator + 1)..];
        return context.Http.Download($"api/buildjobs/{job}/artifacts/{EncodePath(fileName)}", destination, maxBytes, cancel);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var projects = await context.Http.Get($"{Prefix(context)}/projects", AppVeyorContext.Default.ListAppVeyorProject, cancel);
        return new(true, $"{projects.Count} projects");
    }
}
