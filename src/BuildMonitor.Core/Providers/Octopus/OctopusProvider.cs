/// <summary>
/// https://octopus.com/docs/api
/// <para>
/// A row is a deployment: the project is the pipeline, the environment stands in for the
/// branch, and the release version for the run number. Retry and cancel go through the links
/// the server task carries rather than composed paths.
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

    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        if (pipelines.Count == 0 ||
            pipelines[0].Group is not { } spaceId)
        {
            return [];
        }

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

            ProviderEstimate? estimate = null;
            if (task.State == "Executing" &&
                task.Links?.Details is { } details)
            {
                var detail = await context.Http.Get(details, OctopusContext.Default.OctopusTaskDetails, cancel);
                if (detail.Progress is { } progress)
                {
                    estimate = new(
                        null,
                        progress.ProgressPercentage,
                        TimeSpan.TryParse(progress.EstimatedTimeRemaining, CultureInfo.InvariantCulture, out var remaining) ? remaining : null);
                }
            }

            taken[deployment.ProjectId] = soFar + 1;
            environmentNames.TryGetValue(deployment.EnvironmentId, out var environment);
            builds.Add(Convert(context.Connection.Id, server, pipeline, deployment, task, environment, estimate));
        }

        return builds;
    }

    static Build Convert(string connectionId, string server, Pipeline pipeline, OctopusDeployment deployment, OctopusTask task, string? environment, ProviderEstimate? estimate)
    {
        var status = task.State switch
        {
            "Queued" => BuildStatus.Queued,
            "Executing" or "Cancelling" => BuildStatus.Running,
            "Success" => BuildStatus.Succeeded,
            "Failed" or "TimedOut" => BuildStatus.Failed,
            "Canceled" => BuildStatus.Cancelled,
            _ => BuildStatus.Unknown
        };
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
            CanRetry: task.Links?.Rerun is not null && status is not (BuildStatus.Running or BuildStatus.Queued),
            CanCancel: task.Links?.Cancel is not null && status is BuildStatus.Running or BuildStatus.Queued,
            Join(task.Id, task.Links?.Rerun, task.Links?.Cancel));
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
