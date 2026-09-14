/// <summary>
/// https://octopus.com/docs/api
/// <para>
/// A row is a deployment: the project is the pipeline, the environment stands in for the
/// branch, and the release version for the run number.
/// </para>
/// </summary>
sealed class OctopusProvider : ProviderBase
{
    public override ProviderDescriptor Descriptor => ProviderDescriptors.Octopus;

    public override Uri BaseAddress(Connection connection) =>
        new(base.BaseAddress(connection), "api/");

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        var space = await Space(context, cancel);
        var server = context.Http.BaseAddress.GetLeftPart(UriPartial.Authority);
        var projects = await context.Http.Get($"{space.Id}/projects?take=100", OctopusContext.Default.OctopusPageOctopusProject, cancel);
        return projects.Items
            .Select(_ => new Pipeline(_.Id, _.Name, space.Name, space.Id, $"{server}{_.Links?.Web ?? ""}"))
            .ToList();
    }

    static async Task<OctopusSpace> Space(ProviderContext context, Cancel cancel)
    {
        var spaces = await context.Http.Get("spaces?take=100", OctopusContext.Default.OctopusPageOctopusSpace, cancel);
        var wanted = context.Scope("space");
        var space = wanted.Length == 0
            ? spaces.Items.FirstOrDefault(_ => _.IsDefault) ?? spaces.Items.FirstOrDefault()
            : spaces.Items.FirstOrDefault(_ => string.Equals(_.Name, wanted, StringComparison.OrdinalIgnoreCase) || _.Id == wanted);
        return space ?? throw new InvalidOperationException(wanted.Length == 0 ? "No spaces" : $"No space named {wanted}");
    }

    /// <summary>
    /// One dashboard request returns the current and previous deployment of every project to every
    /// environment, where the separate listings took three requests, plus one for each deployment
    /// whose task fell outside the tasks page. A large server can limit the projects its dashboard
    /// returns; then the separate listings are used, so no project goes missing.
    /// </summary>
    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        if (pipelines.Count == 0 ||
            pipelines[0].Group is not { } spaceId)
        {
            return [];
        }

        var projects = string.Join(',', pipelines.Select(_ => _.Id));
        var dashboard = await context.Http.Get($"{spaceId}/dashboard/dynamic?projects={projects}&includePrevious=true", OctopusContext.Default.OctopusDashboard, cancel);
        if (dashboard.ProjectLimit is { } limit &&
            limit < pipelines.Count)
        {
            return await Separately(context, spaceId, pipelines, perPipeline, cancel);
        }

        var web = new Uri(context.Http.BaseAddress, "..").ToString();
        var byProject = pipelines.ToDictionary(_ => _.Id);
        var environments = dashboard.Environments.ToDictionary(_ => _.Id, _ => _.Name);
        var builds = new List<Build>();
        var taken = new Dictionary<string, int>();
        foreach (var item in dashboard.Items.OrderByDescending(_ => _.QueueTime))
        {
            if (!byProject.TryGetValue(item.ProjectId, out var pipeline))
            {
                continue;
            }

            taken.TryGetValue(item.ProjectId, out var soFar);
            if (soFar >= perPipeline)
            {
                continue;
            }

            taken[item.ProjectId] = soFar + 1;
            environments.TryGetValue(item.EnvironmentId, out var environment);
            var estimate = item.State == "Executing"
                ? await Progress(context, $"{spaceId}/tasks/{item.TaskId}/details?verbose=false&tail=1", cancel)
                : null;
            var status = Status(item.State);
            builds.Add(new(
                context.Connection.Id,
                pipeline.Id,
                pipeline.Name,
                pipeline.RepoName,
                environment,
                item.ReleaseVersion ?? "",
                status,
                item.State?.ToLowerInvariant(),
                item.QueueTime,
                item.StartTime ?? item.QueueTime,
                item.CompletedTime,
                estimate,
                $"{web}app#/{spaceId}/deployments/{item.DeploymentId}",
                null,
                null,
                null,
                null,
                $"Deploy {pipeline.Name} release {item.ReleaseVersion} to {environment}",
                null,
                CanRetry: status is not (BuildStatus.Running or BuildStatus.Queued),
                CanCancel: status is BuildStatus.Running or BuildStatus.Queued,
                Join(item.TaskId, $"{spaceId}/tasks/rerun/{item.TaskId}", $"{spaceId}/tasks/{item.TaskId}/cancel"),
                pipeline.Url));
        }

        return builds;
    }

    static async Task<IReadOnlyList<Build>> Separately(ProviderContext context, string spaceId, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var server = context.Http.BaseAddress.GetLeftPart(UriPartial.Authority);
        var byProject = pipelines.ToDictionary(_ => _.Id);
        var environments = await context.Http.Get($"{spaceId}/environments/all", OctopusContext.Default.ListOctopusEnvironment, cancel);
        var environmentNames = environments.ToDictionary(_ => _.Id, _ => _.Name);
        var take = Math.Min(100, perPipeline * pipelines.Count);
        var deployments = await context.Http.Get($"{spaceId}/deployments?take={take}", OctopusContext.Default.OctopusPageOctopusDeployment, cancel);
        var tasks = await context.Http.Get($"{spaceId}/tasks?take={take}&name=Deploy", OctopusContext.Default.OctopusPageOctopusTask, cancel);
        var byTask = tasks.Items.ToDictionary(_ => _.Id);

        var builds = new List<Build>();
        var taken = new Dictionary<string, int>();
        foreach (var deployment in deployments.Items)
        {
            if (!byProject.TryGetValue(deployment.ProjectId, out var pipeline))
            {
                continue;
            }

            taken.TryGetValue(deployment.ProjectId, out var soFar);
            if (soFar >= perPipeline)
            {
                continue;
            }

            if (!byTask.TryGetValue(deployment.TaskId, out var task))
            {
                task = await context.Http.Get($"tasks/{deployment.TaskId}", OctopusContext.Default.OctopusTask, cancel);
            }

            var estimate = task.State == "Executing" && task.Links?.Details is { } details
                ? await Progress(context, Link(details, "verbose=false&tail=1"), cancel)
                : null;
            taken[deployment.ProjectId] = soFar + 1;
            environmentNames.TryGetValue(deployment.EnvironmentId, out var environment);
            builds.Add(Convert(context.Connection.Id, server, pipeline, deployment, task, environment, estimate));
        }

        return builds;
    }

    /// <summary>
    /// Only the progress is wanted, not the activity log the details carry by default.
    /// </summary>
    static async Task<ProviderEstimate?> Progress(ProviderContext context, string path, Cancel cancel)
    {
        var detail = await context.Http.Get(path, OctopusContext.Default.OctopusTaskDetails, cancel);
        if (detail.Progress is not { } progress)
        {
            return null;
        }

        return new(
            null,
            progress.ProgressPercentage,
            TimeSpan.TryParse(progress.EstimatedTimeRemaining, CultureInfo.InvariantCulture, out var remaining) ? remaining : null);
    }

    /// <summary>
    /// Octopus links are URI templates, such as <c>/api/Spaces-1/tasks/ServerTasks-1/details{?verbose,tail,ranges}</c>.
    /// Requested as they stand, the braces are escaped into the path and the server does not
    /// recognise it, which failed every poll while a deployment was executing. The template is cut
    /// off, and the query the caller wants is appended.
    /// </summary>
    static string Link(string link, string? query = null)
    {
        var template = link.IndexOf('{');
        var path = template < 0 ? link : link[..template];
        if (query is null)
        {
            return path;
        }

        return path.Contains('?') ? $"{path}&{query}" : $"{path}?{query}";
    }

    static BuildStatus Status(string? state) =>
        state switch
        {
            "Queued" => BuildStatus.Queued,
            "Executing" or "Cancelling" => BuildStatus.Running,
            "Success" => BuildStatus.Succeeded,
            "Failed" or "TimedOut" => BuildStatus.Failed,
            "Canceled" => BuildStatus.Cancelled,
            _ => BuildStatus.Unknown
        };

    static Build Convert(string connectionId, string server, Pipeline pipeline, OctopusDeployment deployment, OctopusTask task, string? environment, ProviderEstimate? estimate)
    {
        var status = Status(task.State);
        var rerun = task.Links?.Rerun is { } rerunLink ? Link(rerunLink) : null;
        var cancel = task.Links?.Cancel is { } cancelLink ? Link(cancelLink) : null;
        return new(
            connectionId,
            pipeline.Id,
            pipeline.Name,
            pipeline.RepoName,
            environment,
            Release(task.Description),
            status,
            task.State?.ToLowerInvariant(),
            task.QueueTime,
            task.StartTime ?? task.QueueTime,
            task.CompletedTime,
            estimate,
            $"{server}{deployment.Links?.Web ?? task.Links?.Web ?? ""}",
            null,
            null,
            null,
            null,
            task.Description,
            null,
            CanRetry: rerun is not null && status is not (BuildStatus.Running or BuildStatus.Queued),
            CanCancel: cancel is not null && status is BuildStatus.Running or BuildStatus.Queued,
            Join(task.Id, rerun, cancel),
            pipeline.Url);
    }

    /// <summary>
    /// "Deploy Web release 1.2.3 to Production": the version is the run number.
    /// </summary>
    static string Release(string? description)
    {
        if (description is null)
        {
            return "";
        }

        var index = description.IndexOf(" release ", StringComparison.Ordinal);
        if (index < 0)
        {
            return "";
        }

        var rest = description[(index + 9)..];
        var end = rest.IndexOf(" to ", StringComparison.Ordinal);
        return end < 0 ? rest.Trim() : rest[..end].Trim();
    }

    public override Task Retry(ProviderContext context, Build build, Cancel cancel) =>
        context.Http.Send(HttpMethod.Post, Split(build)[1], null, cancel);

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel) =>
        context.Http.Send(HttpMethod.Post, Split(build)[2], null, cancel);

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("users/me", OctopusContext.Default.OctopusUser, cancel);
        return new(true, $"Signed in as {user.DisplayName ?? user.Username}");
    }
}
