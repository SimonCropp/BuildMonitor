/// <summary>
/// https://circleci.com/docs/api/v2/
/// <para>
/// A row is a project and a build is one of its pipelines, as the CircleCI app lists them. A
/// pipeline has no status of its own: it is read from its workflows, the newest of each name,
/// since a rerun adds a workflow to the pipeline rather than replacing the one it reruns.
/// </para>
/// <para>
/// API v2 lists no projects and no step output, so the projects a user follows and a failed
/// step's log are read from API v1.1, which CircleCI still serves beside it.
/// </para>
/// </summary>
sealed class CircleCiProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.CircleCi;

    /// <summary>
    /// The organizations discovery found in scope, which the probe asks for their newest pipelines.
    /// </summary>
    const string organizationsKey = "organizations";

    /// <summary>
    /// How a workflow's status decides its pipeline's, first match first. A pipeline is running
    /// while any workflow is, failed once one has, and passed only when nothing says otherwise. A
    /// workflow on hold is waiting for someone to approve it, for days where nobody does, so it
    /// decides nothing while another workflow has an answer: a build that passed and then waits
    /// to be deployed has passed.
    /// </summary>
    static (string Text, BuildStatus Status)[] precedence =
    [
        ("running", BuildStatus.Running),
        // Still running, with a job already failed.
        ("failing", BuildStatus.Running),
        ("failed", BuildStatus.Failed),
        ("error", BuildStatus.Failed),
        ("unauthorized", BuildStatus.Failed),
        ("canceled", BuildStatus.Cancelled),
        ("success", BuildStatus.Succeeded)
    ];

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var scope = context.Scope("organization");
        var pipelines = new List<Pipeline>();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Gone once CircleCI retires v1.1, and then the organizations' recent pipelines are all
        // there is to find projects by.
        var followed = await GetOrNone(context, "api/v1.1/projects", CircleCiContext.Default.ListCircleCiFollowedProject, cancel) ?? [];
        foreach (var project in followed)
        {
            var organization = $"{ShortVcs(project.VcsType)}/{project.Username}";
            var slug = $"{organization}/{project.Reponame}";
            if (InScope(scope, organization, project.Username) &&
                found.Add(slug))
            {
                pipelines.Add(ToPipeline(context, slug, $"{project.Username}/{project.Reponame}", project.VcsUrl, project.DefaultBranch));
            }
        }

        var collaborations = await context.Http.Get("api/v2/me/collaborations", CircleCiContext.Default.ListCircleCiCollaboration, cancel);
        var organizations = collaborations
            .Where(_ => InScope(scope, _.Slug, _.Name))
            .Select(_ => _.Slug)
            .ToArray();
        context.Memory.Set(organizationsKey, organizations);

        // A project of an organization that is not a GitHub or Bitbucket one is in no v1.1 listing,
        // and is found by having built lately.
        foreach (var organization in organizations)
        {
            var recent = await RecentPipelines(context, organization, cancel);
            foreach (var slug in recent.Select(_ => _.ProjectSlug).Where(_ => _.Length > 0))
            {
                if (found.Add(slug) &&
                    await Project(context, slug, cancel) is { } pipeline)
                {
                    pipelines.Add(pipeline);
                }
            }
        }

        return pipelines;
    }

    /// <summary>
    /// An organization by its slug, gh/VerifyTests, or by its name alone.
    /// </summary>
    static bool InScope(string scope, string slug, string name) =>
        scope.Length == 0 ||
        string.Equals(scope, slug, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scope, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The newest pipelines of the projects the user follows in an organization, newest first. An
    /// organization the user is in but CircleCI builds nothing for has none to list.
    /// </summary>
    static async Task<List<CircleCiPipeline>> RecentPipelines(ProviderContext context, string organization, Cancel cancel)
    {
        var page = await GetOrNone(context, $"api/v2/pipeline?org-slug={Encode(organization)}", CircleCiContext.Default.CircleCiPipelines, cancel);
        return page?.Items ?? [];
    }

    /// <summary>
    /// A project found by its pipelines, which name only its slug. Asked for once: its name and
    /// default branch are remembered, or every discovery would pay a request for each.
    /// </summary>
    static async Task<Pipeline?> Project(ProviderContext context, string slug, Cancel cancel)
    {
        var key = $"project|{slug}";
        if (context.Memory.TryGet<Pipeline>(key, out var remembered))
        {
            return remembered;
        }

        var project = await GetOrNone(context, $"api/v2/project/{EncodePath(slug)}", CircleCiContext.Default.CircleCiProject, cancel);
        if (project is null)
        {
            return null;
        }

        var name = project.OrganizationName is { Length: > 0 } organization ? $"{organization}/{project.Name}" : project.Name;
        var pipeline = ToPipeline(context, slug, name, project.VcsInfo?.VcsUrl, project.VcsInfo?.DefaultBranch);
        context.Memory.Set(key, pipeline);
        return pipeline;
    }

    static Pipeline ToPipeline(ProviderContext context, string slug, string name, string? vcsUrl, string? defaultBranch) =>
        new(slug, name, name, null, $"{Web(context)}/pipelines/{WebSlug(slug)}", RepoAddress(vcsUrl), DefaultBranch: defaultBranch);

    static bool Hosted(ProviderContext context) =>
        context.Http.BaseAddress.Host == "circleci.com";

    /// <summary>
    /// The web app for the API in use: app.circleci.com for the hosted service, the server itself
    /// for CircleCI server.
    /// </summary>
    static string Web(ProviderContext context)
    {
        if (Hosted(context))
        {
            return "https://app.circleci.com";
        }

        return context.Http.BaseAddress.GetLeftPart(UriPartial.Authority);
    }

    static string ShortVcs(string? vcsType) =>
        vcsType switch
        {
            null or "github" => "gh",
            "bitbucket" => "bb",
            _ => vcsType
        };

    /// <summary>
    /// A project slug as the web app's addresses spell it: github/owner/name where the API says
    /// gh/owner/name.
    /// </summary>
    static string WebSlug(string slug)
    {
        var separator = slug.IndexOf('/');
        if (separator < 0)
        {
            return slug;
        }

        var vcs = slug[..separator] switch
        {
            "gh" => "github",
            "bb" => "bitbucket",
            var other => other
        };
        return $"{vcs}{EncodePath(slug[separator..])}";
    }

    /// <summary>
    /// The repository's page, from the address CircleCI gives for it. A project that is not a
    /// GitHub or Bitbucket one can name an address on CircleCI itself, or one that is no address,
    /// and neither is a page to open.
    /// </summary>
    static string? RepoAddress(string? url)
    {
        if (url is null ||
            !Uri.TryCreate(url, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps) ||
            address.Host.EndsWith("circleci.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var page = url.TrimEnd('/');
        if (page.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            page = page[..^".git".Length];
        }

        return page;
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        foreach (var pipeline in pipelines)
        {
            // A project deleted since discovery answers with a 404, which would fail the whole
            // fetch; it has no builds until the next discovery drops it.
            var page = await GetOrNone(
                context,
                $"api/v2/project/{EncodePath(pipeline.Id)}/pipeline",
                CircleCiContext.Default.CircleCiPipelines,
                cancel);
            if (page is null)
            {
                continue;
            }

            foreach (var run in page.Items.Take(perPipeline))
            {
                if (await Fetch(context, pipeline, run, cancel) is { } build)
                {
                    builds.Add(build);
                }
            }

            if (pipeline.DefaultBranch is { } branch &&
                await DefaultRun(context, pipeline, page.Items, perPipeline, branch, cancel) is { } own)
            {
                builds.Add(own);
            }
        }

        return builds;
    }

    static async Task<Build?> Fetch(ProviderContext context, Pipeline pipeline, CircleCiPipeline run, Cancel cancel)
    {
        var workflows = await context.Http.Get($"api/v2/pipeline/{run.Id}/workflow", CircleCiContext.Default.CircleCiWorkflows, cancel);
        return Convert(context, pipeline, run, Latest(workflows));
    }

    /// <summary>
    /// The project's newest pipeline on its default branch, where its last few left it out, as a
    /// burst of pull requests does. The page already fetched holds twenty and only the first few
    /// are shown, so the rest of it is looked through before the branch is asked for. A project
    /// with none since the history cutoff is not asked again for an hour.
    /// </summary>
    static async Task<Build?> DefaultRun(ProviderContext context, Pipeline pipeline, List<CircleCiPipeline> page, int perPipeline, string branch, Cancel cancel)
    {
        var memory = context.Memory;
        if (page.Take(perPipeline).Any(_ => _.Vcs?.Branch == branch))
        {
            DefaultRunMemory.Found(memory, pipeline.Id, branch);
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (DefaultRunMemory.KnownNone(memory, pipeline.Id, branch, now))
        {
            return null;
        }

        var run = page.Skip(perPipeline).FirstOrDefault(_ => _.Vcs?.Branch == branch);
        if (run is null)
        {
            var onBranch = await GetOrNone(
                context,
                $"api/v2/project/{EncodePath(pipeline.Id)}/pipeline?branch={Encode(branch)}",
                CircleCiContext.Default.CircleCiPipelines,
                cancel);
            run = onBranch?.Items.FirstOrDefault();
        }

        if (run is null ||
            await Fetch(context, pipeline, run, cancel) is not { } build ||
            (context.Since is { } since && !HistoryCutoff.Keeps(build, since)))
        {
            DefaultRunMemory.None(memory, pipeline.Id, branch, now);
            return null;
        }

        DefaultRunMemory.Found(memory, pipeline.Id, branch);
        return build;
    }

    /// <summary>
    /// Each organization's newest pipelines, with a project's newest one as its token, which a
    /// push moves. Without it a quiet project waits out its schedule before a push to it shows. A
    /// rerun adds a workflow to a pipeline already listed, so it moves nothing and waits for the
    /// schedule.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        if (!context.Memory.TryGet<string[]>(organizationsKey, out var organizations))
        {
            return null;
        }

        var tokens = ImmutableDictionary.CreateBuilder<string, string>();
        foreach (var organization in organizations)
        {
            // Newest first, so the first one seen of a project is its newest.
            foreach (var run in await RecentPipelines(context, organization, cancel))
            {
                tokens.TryAdd(run.ProjectSlug, run.Id);
            }
        }

        return tokens.ToImmutable();
    }

    /// <summary>
    /// The newest workflow of each name. A rerun is a new workflow of the same name in the same
    /// pipeline, and the one it reran is still listed as failed beside it.
    /// </summary>
    static List<CircleCiWorkflow> Latest(CircleCiWorkflows workflows) =>
        workflows.Items
            .GroupBy(_ => _.Name)
            .Select(_ => _.MaxBy(_ => _.CreatedAt)!)
            .ToList();

    static async Task<List<CircleCiWorkflow>> Latest(ProviderContext context, Build build, Cancel cancel) =>
        Latest(await context.Http.Get($"api/v2/pipeline/{build.ProviderRef}/workflow", CircleCiContext.Default.CircleCiWorkflows, cancel));

    static (BuildStatus Status, string Text) StatusOf(List<CircleCiWorkflow> workflows)
    {
        foreach (var (text, status) in precedence)
        {
            if (workflows.Any(_ => _.Status == text))
            {
                return (status, text);
            }
        }

        // On hold or not run, and nothing else to go by.
        return (BuildStatus.Unknown, workflows[0].Status);
    }

    /// <summary>
    /// Null for a pipeline with no workflows that did not fail to be set up: one just created, which
    /// has them by the next fetch, or one whose configuration filtered every workflow out, which
    /// never ran anything and is no build.
    /// </summary>
    static Build? Convert(ProviderContext context, Pipeline pipeline, CircleCiPipeline run, List<CircleCiWorkflow> workflows)
    {
        BuildStatus status;
        string text;
        DateTimeOffset? started = null;
        var finished = run.CreatedAt;
        if (workflows.Count == 0)
        {
            if (run.State != "errored")
            {
                return null;
            }

            status = BuildStatus.Failed;
            text = run.State;
        }
        else
        {
            (status, text) = StatusOf(workflows);
            started = workflows.Min(_ => _.CreatedAt);
            finished = status is BuildStatus.Running ? null : workflows.Max(_ => _.StoppedAt);
        }

        var vcs = run.Vcs;
        var repo = RepoAddress(vcs?.TargetRepositoryUrl) ?? pipeline.RepoUrl;
        var branch = vcs?.Branch ?? vcs?.Tag;
        // A pull request from a fork is built as the pull request's own ref, and names no review.
        var forkPullRequest = ForkPullRequest(vcs?.Branch);
        var pullRequest = vcs?.ReviewId ?? forkPullRequest;
        var pullRequestUrl = vcs?.ReviewUrl;
        string? branchUrl = null;
        if (repo is not null)
        {
            if (vcs?.Branch is not null &&
                forkPullRequest is null)
            {
                branchUrl = $"{repo}/{BranchPath(repo)}/{vcs.Branch}";
            }

            if (pullRequestUrl is null &&
                forkPullRequest is not null &&
                RepoHosts.MarkOf(repo) == "host-github")
            {
                pullRequestUrl = $"{repo}/pull/{forkPullRequest}";
            }
        }

        return new(
            context.Connection.Id,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            branch,
            run.Number.ToString(),
            status,
            text,
            run.CreatedAt,
            started,
            finished,
            null,
            $"{pipeline.Url}/{run.Number}",
            branchUrl,
            pullRequest,
            pullRequestUrl,
            vcs?.Revision,
            vcs?.Commit?.Subject,
            run.Trigger?.Actor?.Login,
            // A pipeline that failed to be set up has no workflow to rerun.
            CanRetry: workflows.Count > 0 &&
                      status is BuildStatus.Succeeded or BuildStatus.Failed or BuildStatus.Cancelled,
            CanCancel: status is BuildStatus.Running,
            run.Id,
            pipeline.Url,
            repo,
            DefaultBranch: pipeline.DefaultBranch);
    }

    static string? ForkPullRequest(string? branch)
    {
        const string prefix = "pull/";
        if (branch is not null &&
            branch.StartsWith(prefix, StringComparison.Ordinal) &&
            branch.Length > prefix.Length &&
            branch.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0)
        {
            return branch[prefix.Length..];
        }

        return null;
    }

    /// <summary>
    /// A branch's page under the repository's, in the form of the service it is on, read from its
    /// host as the row's mark is.
    /// </summary>
    static string BranchPath(string repo) =>
        RepoHosts.MarkOf(repo) switch
        {
            "host-bitbucket" => "branch",
            "host-gitlab" => "-/tree",
            _ => "tree"
        };

    /// <summary>
    /// Reruns the workflows that failed from their failed jobs, or every workflow from its start
    /// where none did, which is a pipeline that passed or was cancelled.
    /// </summary>
    public override async Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var workflows = await Latest(context, build, cancel);
        var failed = workflows.Where(_ => _.Status is "failed" or "error").ToList();
        var fromFailed = failed.Count > 0;
        foreach (var workflow in fromFailed ? failed : workflows)
        {
            CircleCiRerun rerun = new(fromFailed);
            await context.Http.Send(HttpMethod.Post, $"api/v2/workflow/{workflow.Id}/rerun", HttpJson.Json(rerun, CircleCiContext.Default.CircleCiRerun), cancel);
        }
    }

    public override async Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var workflows = await Latest(context, build, cancel);
        foreach (var workflow in workflows.Where(_ => _.Status is "running" or "failing" or "on_hold"))
        {
            await context.Http.Send(HttpMethod.Post, $"api/v2/workflow/{workflow.Id}/cancel", null, cancel);
        }
    }

    /// <summary>
    /// The output of the failed steps of the failed jobs, each under its job and step. API v2 names
    /// a workflow's jobs and nothing inside them, so the steps and their output come from v1.1, by
    /// its own route rather than by the signed address it also gives for each: that one is on a
    /// storage host, which the credential every request carries has no business reaching.
    /// </summary>
    public override async Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel)
    {
        var project = $"api/v1.1/project/{EncodePath(build.PipelineId)}";
        var logs = new List<(string Name, string Log)>();
        foreach (var workflow in (await Latest(context, build, cancel)).Where(_ => _.Status is "failed" or "error" or "failing"))
        {
            var jobs = await context.Http.Get($"api/v2/workflow/{workflow.Id}/job", CircleCiContext.Default.CircleCiJobs, cancel);
            foreach (var job in jobs.Items.Where(_ => _.Status is "failed" or "timedout" or "infrastructure_fail"))
            {
                if (job.JobNumber is not { } number)
                {
                    continue;
                }

                var detail = await context.Http.Get($"{project}/{number}", CircleCiContext.Default.CircleCiJobDetail, cancel);
                foreach (var step in detail.Steps)
                {
                    foreach (var action in step.Actions.Where(_ => _.HasOutput && (_.Failed == true || _.Status is "failed" or "timedout")))
                    {
                        var log = await context.Http.GetLog($"{project}/{number}/output/{action.Step}/{action.Index}?file=true", cancel);
                        logs.Add(($"{job.Name} / {step.Name}", log));
                    }
                }
            }
        }

        return Sections(logs);
    }

    /// <summary>
    /// How many of a pipeline's jobs are asked for their artifacts. A wide matrix would otherwise
    /// cost a request per leg for files the budget would never reach.
    /// </summary>
    const int maxArtifactJobs = 10;

    /// <summary>
    /// Every job's artifacts, not only the failed ones: the job that broke often stored nothing
    /// while a sibling holds the test report that says why. CircleCI lists no sizes.
    /// </summary>
    public override async Task<IReadOnlyList<BuildArtifact>> ListArtifacts(ProviderContext context, Build build, Cancel cancel)
    {
        var numbers = new List<long>();
        foreach (var workflow in await Latest(context, build, cancel))
        {
            var jobs = await context.Http.Get($"api/v2/workflow/{workflow.Id}/job", CircleCiContext.Default.CircleCiJobs, cancel);
            numbers.AddRange(jobs.Items.Select(_ => _.JobNumber).OfType<long>());
        }

        var artifacts = new List<BuildArtifact>();
        foreach (var number in numbers.Take(maxArtifactJobs))
        {
            var listed = await context.Http.Get($"api/v2/project/{EncodePath(build.PipelineId)}/{number}/artifacts", CircleCiContext.Default.CircleCiArtifacts, cancel);
            // The address is the id: a download is by it and nothing else names the file.
            artifacts.AddRange(listed.Items.Select(_ => new BuildArtifact(_.Url, _.Path, null)));
        }

        return artifacts;
    }

    /// <summary>
    /// An artifact is served from the address the listing gave, which on the hosted service is
    /// another host than the API's and still wants the token. So the token goes only where that
    /// address is CircleCI's: an address anywhere else is refused rather than sent the credential.
    /// </summary>
    public override Task<long> DownloadArtifact(ProviderContext context, Build build, BuildArtifact artifact, Stream destination, long maxBytes, Cancel cancel)
    {
        if (!Uri.TryCreate(artifact.Id, UriKind.Absolute, out var address) ||
            !ServesArtifacts(context, address))
        {
            throw new HttpRequestException($"{Descriptor.Name} listed an artifact at an address that is not its own: {artifact.Id}");
        }

        return context.Http.Download(artifact.Id, destination, maxBytes, cancel);
    }

    static bool ServesArtifacts(ProviderContext context, Uri address)
    {
        var api = context.Http.BaseAddress;
        if (address.Scheme != api.Scheme)
        {
            return false;
        }

        if (string.Equals(address.Host, api.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Hosted(context) &&
               (address.Host.EndsWith(".circle-artifacts.com", StringComparison.OrdinalIgnoreCase) ||
                address.Host.EndsWith(".circleci.com", StringComparison.OrdinalIgnoreCase));
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("api/v2/me", CircleCiContext.Default.CircleCiUser, cancel);
        return new(true, $"Signed in as {user.Login}");
    }
}
