/// <summary>
/// https://developer.atlassian.com/cloud/bitbucket/rest/api-group-pipelines/
/// <para>
/// Bitbucket has no rerun endpoint, so a retry starts a new pipeline for the same commit.
/// </para>
/// </summary>
sealed class BitbucketProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.Bitbucket;

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var workspace = context.Scope("workspace");
        var pipelines = new List<Pipeline>();
        var path = $"repositories/{Encode(workspace)}?role=member&pagelen=100&sort=-updated_on";
        for (var page = 0; page < 5 && path is not null; page++)
        {
            var repositories = await context.Http.Get(path, BitbucketContext.Default.BitbucketPage, cancel);
            pipelines.AddRange(repositories.Values.Select(_ => new Pipeline(
                _.Slug,
                _.FullName,
                _.FullName,
                null,
                $"{_.Links?.Html?.Href}/pipelines")));
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
                $"repositories/{Encode(workspace)}/{pipeline.Id}/pipelines?sort=-created_on&pagelen={perPipeline}",
                BitbucketContext.Default.BitbucketPipelinePage,
                cancel);
            builds.AddRange(page.Values.Select(_ => Convert(context.Connection.Id, pipeline, _)));
        }

        return builds;
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
        var branch = run.Target?.RefType == "branch" ? run.Target.RefName : null;
        var pullRequest = run.Target?.PullRequest?.Id?.ToString();
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            branch ?? run.Target?.RefName,
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
            Join(run.Uuid, run.Target?.RefType, run.Target?.RefName, run.Target?.Commit?.Hash),
            web);
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        var target = parts[1] == "branch"
            ? new BitbucketTarget("pipeline_ref_target", "branch", parts[2], new("commit", parts[3]))
            : new BitbucketTarget("pipeline_commit_target", null, null, new("commit", parts[3]));
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
