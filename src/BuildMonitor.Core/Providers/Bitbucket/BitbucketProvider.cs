/// <summary>
/// https://developer.atlassian.com/cloud/bitbucket/rest/api-group-pipelines/
/// <para>
/// Bitbucket has no rerun endpoint, so a retry starts a new pipeline for the same commit, and for a
/// pull request pipeline, for the same pull request.
/// </para>
/// </summary>
sealed class BitbucketProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.Bitbucket;

    // Only what is read, as the probe already asks: whole repositories and pipelines came back
    // with every link and property Bitbucket has for them.
    const string repositoryFields = "next,values.slug,values.full_name,values.links.html.href,values.mainbranch.name";
    const string pipelineFields = "values.uuid,values.build_number,values.state,values.target.ref_type,values.target.ref_name,values.target.source,values.target.destination,values.target.destination_commit.hash,values.target.commit.hash,values.target.pullrequest.id,values.creator.uuid,values.creator.display_name,values.created_on,values.completed_on";

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var workspace = context.Scope("workspace");
        var pipelines = new List<Pipeline>();
        var path = $"repositories/{Encode(workspace)}?role=member&pagelen=100&sort=-updated_on&fields={repositoryFields}";
        for (var page = 0; page < 5 && path is not null; page++)
        {
            var repositories = await context.Http.Get(path, BitbucketContext.Default.BitbucketPage, cancel);
            pipelines.AddRange(repositories.Values.Select(_ => new Pipeline(
                _.Slug,
                _.FullName,
                _.FullName,
                null,
                $"{_.Links?.Html?.Href}/pipelines",
                _.Links?.Html?.Href,
                DefaultBranch: _.MainBranch?.Name)));
            path = repositories.Next;
        }

        return pipelines;
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var workspace = context.Scope("workspace");
        var builds = new List<Build>();
        foreach (var pipeline in pipelines)
        {
            var page = await context.Http.Get(
                $"repositories/{Encode(workspace)}/{pipeline.Id}/pipelines?sort=-created_on&pagelen={perPipeline}&fields={pipelineFields}",
                BitbucketContext.Default.BitbucketPipelinePage,
                cancel);
            var fetched = new List<Build>();
            foreach (var run in page.Values)
            {
                // A Bitbucket account id is a guid, so the name it arrives with here names it for
                // whoever else is handed that id alone; see IdentityNames.
                context.Identities.Add(run.Creator?.Uuid, run.Creator?.DisplayName);
                fetched.Add(Convert(context.Connection.Id, pipeline, run));
            }

            builds.AddRange(fetched);
            if (pipeline.DefaultBranch is { } branch &&
                fetched.All(_ => _.Branch != branch) &&
                await DefaultRun(context, workspace, pipeline, branch, cancel) is { } own)
            {
                builds.Add(own);
            }
        }

        return builds;
    }

    /// <summary>
    /// The repository's newest pipeline on its main branch, where its last few left it out: a
    /// burst of pull requests, each built as its branch and as the pull request, fills them. Asked
    /// for by branch, which leaves out the pull request pipelines targeting it. Bitbucket allows a
    /// thousand requests an hour, so a repository with none since the history cutoff is not asked
    /// again for an hour.
    /// </summary>
    static async Task<Build?> DefaultRun(ProviderContext context, string workspace, Pipeline pipeline, string branch, Cancel cancel)
    {
        var memory = context.Memory;
        var now = DateTimeOffset.UtcNow;
        if (DefaultRunMemory.KnownNone(memory, pipeline.Id, branch, now))
        {
            return null;
        }

        var page = await GetOrNone(
            context,
            $"repositories/{Encode(workspace)}/{pipeline.Id}/pipelines?sort=-created_on&pagelen=1&target.ref_type=BRANCH&target.ref_name={Encode(branch)}&fields={pipelineFields}",
            BitbucketContext.Default.BitbucketPipelinePage,
            cancel);
        if (page?.Values.FirstOrDefault() is not { } run)
        {
            DefaultRunMemory.None(memory, pipeline.Id, branch, now);
            return null;
        }

        var build = Convert(context.Connection.Id, pipeline, run);
        if (build.Branch != branch ||
            (context.Since is { } since && !HistoryCutoff.Keeps(build, since)))
        {
            DefaultRunMemory.None(memory, pipeline.Id, branch, now);
            return null;
        }

        context.Identities.Add(run.Creator?.Uuid, run.Creator?.DisplayName);
        DefaultRunMemory.Found(memory, pipeline.Id, branch);
        return build;
    }

    /// <summary>
    /// The ten most recently updated repositories, with updated_on as the token. Bitbucket allows a
    /// thousand requests an hour and charges one per repository fetched, so a quiet repository waits
    /// up to thirty minutes; a push moves updated_on within seconds, and this one request a minute
    /// fetches that repository at once. Pipelines started without a push wait for the schedule.
    /// </summary>
    public override async Task<ImmutableDictionary<string, string>?> RecentActivity(ProviderContext context, ImmutableArray<PollGroup> groups, ImmutableDictionary<string, string> previous, Cancel cancel)
    {
        var page = await context.Http.Get(
            $"repositories/{Encode(context.Scope("workspace"))}?role=member&sort=-updated_on&pagelen=10&fields=values.slug,values.updated_on",
            BitbucketContext.Default.BitbucketPage,
            cancel);
        return page.Values
            .Where(_ => _.UpdatedOn is not null)
            .ToImmutableDictionary(_ => _.Slug, _ => _.UpdatedOn!.Value.ToString("O", CultureInfo.InvariantCulture));
    }

    static Build Convert(string connectionId, Pipeline pipeline, BitbucketPipeline run)
    {
        var state = run.State?.Name;
        var result = run.State?.Result?.Name;
        var status = state switch
        {
            "PENDING" => BuildStatus.Queued,
            "IN_PROGRESS" => BuildStatus.Running,
            "COMPLETED" => result switch
            {
                "SUCCESSFUL" => BuildStatus.Succeeded,
                "FAILED" or "ERROR" => BuildStatus.Failed,
                "STOPPED" or "EXPIRED" => BuildStatus.Cancelled,
                _ => BuildStatus.Unknown
            },
            _ => BuildStatus.Unknown
        };
        var web = $"https://bitbucket.org/{pipeline.RepoName}";
        var pullRequest = run.Target?.PullRequest?.Id?.ToString();
        // A pull request pipeline names the branch it came from as its source rather than as a
        // ref, and with none read every pull request of a repository shared one empty branch.
        // Bitbucket builds no pull request from a fork, so the source is always this repository's.
        var branch = run.Target?.RefType == "branch" ? run.Target.RefName : run.Target?.Source;
        var name = branch ?? run.Target?.RefName;
        if (name is null &&
            pullRequest is not null)
        {
            name = PullRequestBranches.Unnamed(pullRequest);
        }

        // A pull request pipeline names no ref. What stands in for one is what a retry has to send
        // back: both branches, both commits and the pull request.
        var providerRef = pullRequest is null
            ? Join(run.Uuid, run.Target?.RefType, run.Target?.RefName, run.Target?.Commit?.Hash)
            : Join(run.Uuid, "pullrequest", run.Target?.Source, run.Target?.Commit?.Hash, run.Target?.Destination, run.Target?.DestinationCommit?.Hash, pullRequest);
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            name,
            run.BuildNumber.ToString(),
            status,
            (result ?? state)?.ToLowerInvariant(),
            run.CreatedOn,
            run.CreatedOn,
            run.CompletedOn,
            null,
            $"{web}/pipelines/results/{run.BuildNumber}",
            branch is null ? null : $"{web}/branch/{branch}",
            pullRequest,
            pullRequest is null ? null : $"{web}/pull-requests/{pullRequest}",
            run.Target?.Commit?.Hash,
            null,
            run.Creator?.DisplayName,
            CanRetry: state == "COMPLETED" && run.Target?.Commit?.Hash is not null,
            CanCancel: state is "PENDING" or "IN_PROGRESS",
            providerRef,
            pipeline.Url,
            pipeline.RepoUrl ?? web,
            DefaultBranch: pipeline.DefaultBranch);
    }

    /// <summary>
    /// bitbucket.org, where every repository the API serves is.
    /// </summary>
    public override Uri RepositoryRoot(Connection connection) =>
        new("https://bitbucket.org/");

    /// <summary>
    /// What became of a failed branch of a repository on bitbucket.org. A pull request is asked for
    /// its state, declined and superseded both being closed. Any other branch is asked whether it,
    /// or a tag of its name, is still there, and then whether the repository is, since Bitbucket
    /// answers a repository the token cannot see with the same 404 as a missing branch. Bitbucket
    /// builds no pull request from a fork, so a fork's branch never reaches here.
    /// </summary>
    public override async Task<BranchFate> FateOf(ProviderContext context, BranchQuestion question, Cancel cancel)
    {
        if (RepositoryPath(context, question.Repository) is not { } path ||
            path.Count(_ => _ == '/') != 1)
        {
            return BranchFate.Unknown;
        }

        var repository = $"repositories/{EncodePath(path)}";
        if (question.PullRequest is { } number)
        {
            var pullRequest = await GetOrNone(context, $"{repository}/pullrequests/{Encode(number)}", BitbucketContext.Default.BitbucketPullRequest, cancel);
            return pullRequest?.State switch
            {
                "OPEN" => BranchFate.Open,
                "MERGED" => BranchFate.Merged,
                "DECLINED" or "SUPERSEDED" => BranchFate.Closed,
                _ => BranchFate.Unknown
            };
        }

        if (question.Branch.Contains(':'))
        {
            return BranchFate.Unknown;
        }

        // A branch's slashes as they are: the route takes the rest of the path as the name.
        var name = EncodePath(question.Branch);
        if (await GetOrNone(context, $"{repository}/refs/branches/{name}", BitbucketContext.Default.BitbucketBranch, cancel) is not null ||
            await GetOrNone(context, $"{repository}/refs/tags/{name}", BitbucketContext.Default.BitbucketBranch, cancel) is not null)
        {
            return BranchFate.Open;
        }

        if (await GetOrNone(context, repository, BitbucketContext.Default.BitbucketRepository, cancel) is null)
        {
            return BranchFate.Unknown;
        }

        return BranchFate.Deleted;
    }

    /// <summary>
    /// A pull request pipeline names no ref, and sent back as its commit alone it ran that commit's
    /// default pipeline, outside the pull request. So it goes back as a pull request target, with
    /// both branches and both commits, since Bitbucket refuses one missing any of them.
    /// </summary>
    public override Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var commit = new BitbucketTargetCommit("commit", parts[3]);
        var target = parts[1] switch
        {
            "branch" => new BitbucketTarget("pipeline_ref_target", "branch", parts[2], commit),
            "pullrequest" => new BitbucketTarget(
                "pipeline_pullrequest_target",
                null,
                null,
                commit,
                Source: parts[2],
                Destination: parts[4],
                DestinationCommit: new("commit", parts[5]),
                PullRequest: new(long.Parse(parts[6]))),
            _ => new BitbucketTarget("pipeline_commit_target", null, null, commit)
        };
        var body = HttpJson.Json(new(target), BitbucketContext.Default.BitbucketTrigger);
        return context.Http.Send(HttpMethod.Post, $"repositories/{Encode(context.Scope("workspace"))}/{build.PipelineId}/pipelines", body, cancel);
    }

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var uuid = Split(build)[0];
        return context.Http.Send(HttpMethod.Post, $"repositories/{Encode(context.Scope("workspace"))}/{build.PipelineId}/pipelines/{Encode(uuid)}/stopPipeline", null, cancel);
    }

    /// <summary>
    /// The logs of the pipeline's failed steps. A log request that accepts JSON or plain text is
    /// refused with a 406, so it accepts anything. A finished step's log is a redirect to storage that
    /// wants no credential, which the handler follows.
    /// </summary>
    public override async Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel)
    {
        var pipeline = $"repositories/{Encode(context.Scope("workspace"))}/{build.PipelineId}/pipelines/{Encode(Split(build)[0])}";
        var logs = new List<(string Name, string Log)>();
        var path = $"{pipeline}/steps";
        for (var page = 0; page < 10 && path is not null; page++)
        {
            var steps = await context.Http.Get(path, BitbucketContext.Default.BitbucketStepPage, cancel);
            foreach (var step in steps.Values.Where(_ => _.State?.Result?.Name is "FAILED" or "ERROR"))
            {
                logs.Add((step.Name ?? step.Uuid, await context.Http.GetLog($"{pipeline}/steps/{Encode(step.Uuid)}/log", cancel, "*/*")));
            }

            path = steps.Next;
        }

        return Sections(logs);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var workspace = await context.Http.Get($"workspaces/{Encode(context.Scope("workspace"))}", BitbucketContext.Default.BitbucketWorkspace, cancel);
        return new(true, $"Workspace {workspace.Name}");
    }
}
