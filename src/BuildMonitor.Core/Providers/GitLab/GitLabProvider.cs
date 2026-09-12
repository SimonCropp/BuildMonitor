/// <summary>
/// https://docs.gitlab.com/api/pipelines/
/// </summary>
sealed class GitLabProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.GitLab;

    public override Uri BaseAddress(Connection connection) =>
        new(base.BaseAddress(connection), "api/v4/");

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var group = context.Scope("group");
        var path = group.Length == 0
            ? "projects?membership=true&simple=true&archived=false&order_by=last_activity_at&per_page=100"
            : $"groups/{Encode(group)}/projects?include_subgroups=true&simple=true&archived=false&order_by=last_activity_at&per_page=100";
        var projects = await context.Http.Get(path, GitLabContext.Default.ListGitLabProject, cancel);
        return projects
            .Select(_ => new Pipeline(_.Id.ToString(), _.PathWithNamespace, _.PathWithNamespace, null, $"{_.WebUrl}/-/pipelines"))
            .ToList();
    }

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var builds = new List<Build>();
        foreach (var pipeline in pipelines)
        {
            var runs = await context.Http.Get($"projects/{pipeline.Id}/pipelines?per_page={perPipeline}", GitLabContext.Default.ListGitLabPipeline, cancel);
            foreach (var run in runs)
            {
                // The listing carries no timings; only a live run is worth the second call.
                if (run.Status is "running" or "pending")
                {
                    var detail = await context.Http.Get($"projects/{pipeline.Id}/pipelines/{run.Id}", GitLabContext.Default.GitLabPipeline, cancel);
                    run.StartedAt = detail.StartedAt;
                    run.FinishedAt = detail.FinishedAt;
                }

                builds.Add(Convert(context.Connection.Id, pipeline, run));
            }
        }

        return builds;
    }

    static Build Convert(string connectionId, Pipeline pipeline, GitLabPipeline run)
    {
        var status = run.Status switch
        {
            "created" or "waiting_for_resource" or "preparing" or "pending" or "scheduled" or "waiting_for_callback" => BuildStatus.Queued,
            "running" or "canceling" => BuildStatus.Running,
            "success" => BuildStatus.Succeeded,
            "failed" => BuildStatus.Failed,
            "canceled" => BuildStatus.Cancelled,
            _ => BuildStatus.Unknown
        };
        var web = pipeline.Url[..pipeline.Url.LastIndexOf("/-/", StringComparison.Ordinal)];
        string? mergeRequest = null;
        var branch = run.Ref;
        if (run.Ref is not null &&
            run.Ref.StartsWith("refs/merge-requests/", StringComparison.Ordinal))
        {
            mergeRequest = run.Ref.Split('/')[2];
            branch = null;
        }

        var finished = status is BuildStatus.Succeeded or BuildStatus.Failed or BuildStatus.Cancelled ? run.FinishedAt ?? run.UpdatedAt : null;
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            branch,
            run.Iid.ToString(),
            status,
            run.Status,
            run.CreatedAt,
            run.StartedAt ?? run.CreatedAt,
            finished,
            null,
            run.WebUrl,
            branch is null ? null : $"{web}/-/tree/{branch}",
            mergeRequest,
            mergeRequest is null ? null : $"{web}/-/merge_requests/{mergeRequest}",
            run.Sha,
            run.Name,
            null,
            CanRetry: status is BuildStatus.Failed or BuildStatus.Cancelled,
            CanCancel: status is BuildStatus.Queued or BuildStatus.Running,
            Join(pipeline.Id, run.Id.ToString()));
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Send(HttpMethod.Post, $"projects/{parts[0]}/pipelines/{parts[1]}/retry", null, cancel);
    }

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel)
    {
        var parts = Split(build);
        return context.Http.Send(HttpMethod.Post, $"projects/{parts[0]}/pipelines/{parts[1]}/cancel", null, cancel);
    }

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("user", GitLabContext.Default.GitLabUser, cancel);
        return new(true, $"Signed in as {user.Username}");
    }
}
