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

    // The project limit a dashboard reported, and the space's environments, both kept until the
    // projects are listed again.
    const string dashboardLimit = "octopus.dashboard-limit";
    const string environmentNames = "octopus.environments";

    // The grants of TaskCancel in the connection's space, as the last check found them.
    const string taskCancel = "octopus.task-cancel";

    public override Uri BaseAddress(Connection connection) =>
        new(base.BaseAddress(connection), "api/");

    public override async Task<IReadOnlyList<Pipeline>> DiscoverPipelines(ProviderContext context, Cancel cancel)
    {
        // A listing may have found projects a limited dashboard now has room for, or a space with
        // environments added since.
        context.Memory.Remove(dashboardLimit);
        context.Memory.Remove(environmentNames);
        var space = await Space(context, cancel);
        var server = context.Http.BaseAddress.GetLeftPart(UriPartial.Authority);
        var projects = await context.Http.Get($"{space.Id}/projects?take=100", OctopusContext.Default.OctopusPageOctopusProject, cancel);
        // The project stands in for the repository. The space did before, and grouped every finished
        // deployment in it under the space's name.
        return projects.Items
            .Select(_ => new Pipeline(_.Id, _.Name, _.Name, space.Id, $"{server}{_.Links?.Web ?? ""}"))
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
    /// environment, where the separate listings took three requests, plus one for the deployments
    /// whose tasks fell outside the tasks page. A large server can limit the projects its dashboard
    /// returns; then the separate listings are used, so no project goes missing, and the limit is
    /// remembered until the projects are listed again, as the dashboard was otherwise downloaded
    /// every poll only to be thrown away.
    /// </summary>
    public override async Task<IReadOnlyList<Build>> FetchBuilds(ProviderContext context, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        if (pipelines.Count == 0 ||
            pipelines[0].Group is not { } spaceId)
        {
            return [];
        }

        if (context.Memory.TryGet<int>(dashboardLimit, out var known) &&
            known < pipelines.Count)
        {
            return await Separately(context, spaceId, pipelines, perPipeline, cancel);
        }

        var projects = string.Join(',', pipelines.Select(_ => _.Id));
        var dashboard = await context.Http.Get($"{spaceId}/dashboard/dynamic?projects={projects}&includePrevious=true", OctopusContext.Default.OctopusDashboard, cancel);
        if (dashboard.ProjectLimit is { } limit &&
            limit < pipelines.Count)
        {
            context.Memory.Set(dashboardLimit, limit);
            return await Separately(context, spaceId, pipelines, perPipeline, cancel);
        }

        var web = new Uri(context.Http.BaseAddress, "..").ToString();
        var byProject = pipelines.ToDictionary(_ => _.Id);
        var environments = dashboard.Environments.ToDictionary(_ => _.Id, _ => _.Name);
        var grants = CancelGrants(context, spaceId);
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
                CanRetry: false,
                CanCancel: status is BuildStatus.Running or BuildStatus.Queued &&
                           MayCancel(grants, item.ProjectId, item.EnvironmentId),
                Join(item.TaskId, $"{spaceId}/tasks/rerun/{item.TaskId}", $"{spaceId}/tasks/{item.TaskId}/cancel", $"{spaceId}/tasks/{item.TaskId}/raw"),
                pipeline.Url));
        }

        return builds;
    }

    static async Task<IReadOnlyList<Build>> Separately(ProviderContext context, string spaceId, IReadOnlyList<Pipeline> pipelines, int perPipeline, Cancel cancel)
    {
        var server = context.Http.BaseAddress.GetLeftPart(UriPartial.Authority);
        var byProject = pipelines.ToDictionary(_ => _.Id);
        var environments = await Environments(context, spaceId, cancel);
        var grants = CancelGrants(context, spaceId);
        var take = Math.Min(100, perPipeline * pipelines.Count);
        var deployments = await context.Http.Get($"{spaceId}/deployments?take={take}", OctopusContext.Default.OctopusPageOctopusDeployment, cancel);
        var tasks = await context.Http.Get($"{spaceId}/tasks?take={take}&name=Deploy", OctopusContext.Default.OctopusPageOctopusTask, cancel);
        var byTask = tasks.Items.ToDictionary(_ => _.Id);

        var chosen = new List<(Pipeline Pipeline, OctopusDeployment Deployment)>();
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

            taken[deployment.ProjectId] = soFar + 1;
            chosen.Add((pipeline, deployment));
        }

        // Together, where each was a request of its own, and from the space: a route without it
        // reads the default space only.
        var missing = chosen
            .Select(_ => _.Deployment.TaskId)
            .Where(_ => !byTask.ContainsKey(_))
            .Distinct()
            .ToList();
        if (missing.Count > 0)
        {
            var fetched = await context.Http.Get($"{spaceId}/tasks?ids={string.Join(',', missing)}&take={missing.Count}", OctopusContext.Default.OctopusPageOctopusTask, cancel);
            foreach (var task in fetched.Items)
            {
                byTask[task.Id] = task;
            }
        }

        var builds = new List<Build>();
        foreach (var (pipeline, deployment) in chosen)
        {
            // A task the server no longer has leaves nothing to show for its deployment.
            if (!byTask.TryGetValue(deployment.TaskId, out var task))
            {
                continue;
            }

            var estimate = task.State == "Executing" && task.Links?.Details is { } details
                ? await Progress(context, Link(details, "verbose=false&tail=1"), cancel)
                : null;
            environments.TryGetValue(deployment.EnvironmentId, out var environment);
            var mayCancel = MayCancel(grants, deployment.ProjectId, deployment.EnvironmentId);
            builds.Add(Convert(context.Connection.Id, server, spaceId, pipeline, deployment, task, environment, estimate, mayCancel));
        }

        return builds;
    }

    /// <summary>
    /// The space's environment names by id, read once until the projects are listed again. Read
    /// every poll, they changed no more often than the projects do.
    /// </summary>
    static async Task<IReadOnlyDictionary<string, string>> Environments(ProviderContext context, string spaceId, Cancel cancel)
    {
        if (context.Memory.TryGet<IReadOnlyDictionary<string, string>>(environmentNames, out var known))
        {
            return known;
        }

        var environments = await context.Http.Get($"{spaceId}/environments/all", OctopusContext.Default.ListOctopusEnvironment, cancel);
        var names = environments.ToDictionary(_ => _.Id, _ => _.Name);
        context.Memory.Set(environmentNames, names);
        return names;
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

    static Build Convert(string connectionId, string server, string spaceId, Pipeline pipeline, OctopusDeployment deployment, OctopusTask task, string? environment, ProviderEstimate? estimate, bool mayCancel)
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
            CanRetry: false,
            CanCancel: mayCancel && cancel is not null && status is BuildStatus.Running or BuildStatus.Queued,
            Join(task.Id, rerun, cancel, task.Links?.Raw is { } raw ? Link(raw) : $"{spaceId}/tasks/{task.Id}/raw"),
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

    /// <summary>
    /// Never offered: Octopus answers a rerun of a deployment task with 400 "This task cannot be
    /// re-run". Deploying the release again would be a new deployment, not a retry of this one.
    /// </summary>
    public override Task Retry(ProviderContext context, Build build, Cancel cancel) =>
        throw new NotSupportedException("Octopus deployments can not be retried");

    public override Task Cancel(ProviderContext context, Build build, Cancel cancel) =>
        context.Http.Send(HttpMethod.Post, Split(build)[2], null, cancel);

    /// <summary>
    /// The deployment task's whole log, the one log Octopus keeps for it, read from the task's own
    /// space: the route without a space reads the default space only. Asked for as a file, because
    /// the call can also answer in JSON.
    /// </summary>
    public override Task<string> FetchLog(ProviderContext context, Build build, Cancel cancel) =>
        context.Http.GetLog(Split(build)[3], cancel, "application/octet-stream");

    public override async Task<ConnectionTest> Test(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("users/me", OctopusContext.Default.OctopusUser, cancel);
        return new(true, $"Signed in as {user.DisplayName ?? user.Username}", await AccessOrUnknown(context, cancel));
    }

    /// <summary>
    /// An API key acts with its user's permissions, which users/{id}/permissions lists with where
    /// each grant applies. Cancelling a deployment needs TaskCancel, and a retry is never offered.
    /// The grants in the connection's space are kept for the fetch, which applies those limited to
    /// projects or environments per deployment. An answer the server marks incomplete decides
    /// nothing, and neither does a system administrator's, whose rights in a space it may not list.
    /// </summary>
    public override async Task<BuildAccess> Access(ProviderContext context, Cancel cancel)
    {
        var user = await context.Http.Get("users/me", OctopusContext.Default.OctopusUser, cancel);
        var space = await Space(context, cancel);
        var permissions = await context.Http.Get(
            $"users/{Encode(user.Id)}/permissions?spaces={Encode(space.Id)}&includeSystem=true",
            OctopusContext.Default.OctopusPermissions,
            cancel);
        if (!permissions.IsPermissionsComplete ||
            (permissions.SystemPermissions?.Contains("AdministerSystem") ?? false))
        {
            context.Memory.Remove(taskCancel);
            return BuildAccess.Unknown;
        }

        List<OctopusGrant> grants = [];
        if (permissions.SpacePermissions?.GetValueOrDefault("TaskCancel") is { } granted)
        {
            grants = [..granted.Where(_ => _.SpaceId is null || _.SpaceId == space.Id)];
        }

        context.Memory.Set(taskCancel, (space.Id, grants));
        if (grants.Count == 0)
        {
            return BuildAccess.Watch;
        }

        if (grants.Any(Unrestricted))
        {
            return BuildAccess.Change;
        }

        return BuildAccess.Unknown;
    }

    static bool Unrestricted(OctopusGrant grant) =>
        grant.RestrictedToProjectIds is not { Count: > 0 } &&
        grant.RestrictedToEnvironmentIds is not { Count: > 0 } &&
        grant.RestrictedToTenantIds is not { Count: > 0 } &&
        grant.RestrictedToProjectGroupIds is not { Count: > 0 };

    /// <summary>
    /// The grants of TaskCancel the last check found in the space, or null when it found nothing
    /// to go by.
    /// </summary>
    static List<OctopusGrant>? CancelGrants(ProviderContext context, string spaceId)
    {
        if (context.Memory.TryGet<(string SpaceId, List<OctopusGrant> Grants)>(taskCancel, out var known) &&
            known.SpaceId == spaceId)
        {
            return known.Grants;
        }

        return null;
    }

    /// <summary>
    /// Whether a grant applies to a deployment of the project to the environment. A grant limited
    /// to tenants or project groups is taken to, as which a deployment belongs to is not known here.
    /// </summary>
    static bool MayCancel(List<OctopusGrant>? grants, string projectId, string environmentId) =>
        grants is null ||
        grants.Any(_ => Covers(_.RestrictedToProjectIds, projectId) &&
                        Covers(_.RestrictedToEnvironmentIds, environmentId));

    static bool Covers(List<string>? restriction, string id) =>
        restriction is not { Count: > 0 } ||
        restriction.Contains(id);
}
