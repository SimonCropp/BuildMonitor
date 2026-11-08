/// <summary>
/// What the MCP tools do, over the protocol, with no MCP types in sight so it can be tested
/// against an in-process tray.
/// </summary>
sealed class MonitorTools(IProtocolClient client)
{
    public Task<List<BuildDto>> ListBuilds(string? filter, Cancel cancel) =>
        ListBuilds(filter, false, cancel);

    /// <summary>
    /// With <paramref name="includeDeferred"/>, the failures the user put off follow the rest, each
    /// carrying when its deferral ends. Left out by default, as the window leaves them out.
    /// </summary>
    public async Task<List<BuildDto>> ListBuilds(string? filter, bool includeDeferred, Cancel cancel)
    {
        var builds = await Read(new(Verb.List), DtoContext.Default.ListBuildDto, cancel);
        if (includeDeferred)
        {
            builds.AddRange(await ListDeferred(null, cancel));
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            return builds;
        }

        return builds
            .Where(_ => Matches(_, filter))
            .ToList();
    }

    /// <summary>
    /// The failures the user put off for a few days, which list_builds and the failing counts leave
    /// out until the deferral ends or the pipeline passes.
    /// </summary>
    public async Task<List<BuildDto>> ListDeferred(string? filter, Cancel cancel)
    {
        var builds = await Read(new(Verb.Deferred), DtoContext.Default.ListBuildDto, cancel);
        if (string.IsNullOrWhiteSpace(filter))
        {
            return builds;
        }

        return builds
            .Where(_ => Matches(_, filter))
            .ToList();
    }

    /// <summary>
    /// The list as list_builds gives it. A filter lists the passing builds it matches too, since
    /// it names what the assistant asked about, and "is main green on Verify?" is answered by the
    /// passing build alone.
    /// </summary>
    public async Task<BuildListDto> BuildList(string? filter, bool includeDeferred, bool includePassing, Cancel cancel)
    {
        var builds = await ListBuilds(filter, includeDeferred, cancel);
        var listPassing = includePassing ||
                          !string.IsNullOrWhiteSpace(filter);
        var rows = builds
            .Where(_ => listPassing ||
                        _.Status != nameof(BuildStatus.Succeeded))
            .Select(Row)
            .ToList();
        return new(builds.Count - rows.Count, rows);
    }

    static BuildRowDto Row(BuildDto build) =>
        new(
            build.Key,
            build.Connection,
            build.Pipeline,
            build.Repo,
            build.Branch,
            build.Run,
            build.Status,
            build.Finished,
            build.Timing,
            build.OtherBranch,
            build.DeferredUntil);

    static bool Matches(BuildDto build, string filter) =>
        build.Pipeline.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        build.Repo.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        build.Connection.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        (build.Branch?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// The pipelines whose own run failed, as the tray and the summary count them. A pull request
    /// failing is in the list of builds, marked as another branch, but not here: the triage prompt
    /// works through these a pipeline at a time, and one repository's main and its pull request
    /// would read as two repositories broken by one pipeline.
    /// </summary>
    public async Task<List<BuildDto>> ListFailing(string? filter, Cancel cancel)
    {
        var builds = await Read(new(Verb.List), DtoContext.Default.ListBuildDto, cancel);
        var failed = builds.Where(_ => _.Status == nameof(BuildStatus.Failed) &&
                                       _.OtherBranch != true);
        if (string.IsNullOrWhiteSpace(filter))
        {
            return failed.ToList();
        }

        return failed
            .Where(_ => Matches(_, filter))
            .ToList();
    }

    public Task<BuildDto> GetBuild(string key, Cancel cancel) =>
        Read(new(Verb.Get, key), DtoContext.Default.BuildDto, cancel);

    /// <summary>
    /// The runs of one pipeline rather than the one row it shows. Runs on the same branch share
    /// a key, which is the row's, and are told apart by their run number.
    /// </summary>
    public Task<List<BuildDto>> ListRuns(string key, Cancel cancel) =>
        Read(new(Verb.Runs, key), DtoContext.Default.ListBuildDto, cancel);

    /// <summary>
    /// How often a wait asks the tray how the build stands. The tray answers from what it holds, so
    /// asking costs no call to the CI service, and it polls a running build often near when it
    /// should finish: asking more often would mostly read the same poll again.
    /// </summary>
    public TimeSpan WaitInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Waits for a build to finish, so an assistant asked to say when it is done can answer then,
    /// rather than listing it again and again, each list a turn of the user's.
    /// <para>
    /// The run is followed by its number, not its key. Runs on one branch share the key, so a push
    /// that started the next run as this one finished would otherwise be waited on in its place. A
    /// run the key no longer shows, and a pull request's run that passed, which leaves the list,
    /// are found among the pipeline's runs instead.
    /// </para>
    /// <para>
    /// With <paramref name="nextRun"/>, a run that had already finished is not the answer. Right
    /// after a push or a retry the tray has not polled the new run yet and still shows the last one,
    /// and an assistant handed that would report the push's result before it had run.
    /// </para>
    /// </summary>
    /// <param name="waiting">Told how long the wait has run and what it is waiting on, before each pause.</param>
    public async Task<BuildWaitDto> WaitForBuild(string key, bool nextRun, TimeSpan timeout, Action<TimeSpan, string>? waiting, Cancel cancel)
    {
        var watch = Stopwatch.StartNew();
        var build = await Latest(key, cancel);
        if (nextRun &&
            !IsActive(build))
        {
            var last = build;
            while (!IsAnotherRun(build, last))
            {
                if (!await Pause($"Waiting for the run after {last.Pipeline} {last.Run}", watch, timeout, waiting, cancel))
                {
                    return new(true, build);
                }

                build = await Latest(key, cancel);
            }
        }

        var run = build.Run;
        while (IsActive(build))
        {
            if (!await Pause($"{build.Pipeline} {build.Run}: {build.Status}, {build.Timing}", watch, timeout, waiting, cancel))
            {
                return new(true, build);
            }

            build = await Find(key, run, cancel);
        }

        return new(false, build);
    }

    static bool IsActive(BuildDto build) =>
        build.Status is nameof(BuildStatus.Queued) or nameof(BuildStatus.Running);

    /// <summary>
    /// Whether the key now shows a run other than <paramref name="last"/>, which had finished: a new
    /// number, or the same one going again or finished at another time, as a retry is on a service
    /// that keeps the number. A run short enough to start and finish between two polls is seen only
    /// by that.
    /// </summary>
    static bool IsAnotherRun(BuildDto build, BuildDto last) =>
        build.Run != last.Run ||
        IsActive(build) ||
        build.Finished != last.Finished;

    /// <summary>
    /// False, without waiting, once the timeout has passed.
    /// </summary>
    async Task<bool> Pause(string message, Stopwatch watch, TimeSpan timeout, Action<TimeSpan, string>? waiting, Cancel cancel)
    {
        var waited = watch.Elapsed;
        var left = timeout - waited;
        if (left <= TimeSpan.Zero)
        {
            return false;
        }

        waiting?.Invoke(waited, message);
        await Task.Delay(left < WaitInterval ? left : WaitInterval, cancel);
        return true;
    }

    /// <summary>
    /// The run the key shows, or, where it shows none, the newest of the pipeline's runs on that
    /// key: a pull request's run is listed only while it is running, queued or failed.
    /// </summary>
    async Task<BuildDto> Latest(string key, Cancel cancel)
    {
        if (await Shown(key, cancel) is { } shown)
        {
            return shown;
        }

        var runs = await ListRuns(key, cancel);
        return runs.FirstOrDefault(_ => _.Key == key) ??
               throw new InvalidOperationException($"No build with key {key}");
    }

    /// <summary>
    /// One run of the key, by its number, whether or not it is still the run the key shows.
    /// </summary>
    async Task<BuildDto> Find(string key, string run, Cancel cancel)
    {
        if (await Shown(key, cancel) is { } shown &&
            shown.Run == run)
        {
            return shown;
        }

        var runs = await ListRuns(key, cancel);
        return runs.FirstOrDefault(_ => _.Key == key && _.Run == run) ??
               throw new InvalidOperationException($"Run {run} of {key} is no longer held");
    }

    /// <summary>
    /// The run the key shows, or null where it shows none.
    /// </summary>
    async Task<BuildDto?> Shown(string key, Cancel cancel)
    {
        var response = await client.Send(new(Verb.Get, key), cancel);
        if (!response.Ok)
        {
            return null;
        }

        return response.Read(DtoContext.Default.BuildDto);
    }

    public Task<List<PipelineDto>> ListPipelines(Cancel cancel) =>
        Read(new(Verb.Pipelines), DtoContext.Default.ListPipelineDto, cancel);

    public Task<SummaryDto> Summary(Cancel cancel) =>
        Read(new(Verb.Summary), DtoContext.Default.SummaryDto, cancel);

    public Task<List<ConnectionDto>> ListConnections(Cancel cancel) =>
        Read(new(Verb.Connections), DtoContext.Default.ListConnectionDto, cancel);

    public async Task<string> Refresh(string? connectionId, Cancel cancel)
    {
        await Send(new(Verb.Refresh, connectionId), cancel);
        if (connectionId is null)
        {
            return "Refreshing every connection";
        }

        return $"Refreshing {connectionId}";
    }

    public Task<string> RetryBuild(string key, Cancel cancel) =>
        Send(new(Verb.Retry, key), cancel);

    public Task<string> CancelBuild(string key, Cancel cancel) =>
        Send(new(Verb.Cancel, key), cancel);

    public Task<string> RunBuildNext(string key, Cancel cancel) =>
        Send(new(Verb.RunNext, key), cancel);

    public Task<string> OpenBuild(string key, string which, Cancel cancel) =>
        Send(new(Verb.Open, key, which), cancel);

    /// <summary>
    /// The log as the text the CI service wrote, rather than JSON: its sections and indentation
    /// are what an assistant reads the failure out of, and escaping them buys nothing.
    /// </summary>
    public Task<string> GetLog(string key, int maxLines, Cancel cancel) =>
        Send(new(Verb.Log, key, maxLines.ToString(CultureInfo.InvariantCulture)), cancel);

    /// <summary>
    /// The files rather than their bytes: the protocol base64s a whole message into one line and
    /// buffers it on both sides, so an archive through it would sit in memory twice and expanded.
    /// Whoever asked reads them from disk with its own file tools.
    /// </summary>
    public Task<TriageFilesDto> DownloadArtifacts(string key, Cancel cancel) =>
        Read(new(Verb.Triage, key), DtoContext.Default.TriageFilesDto, cancel);

    async Task<T> Read<T>(Message message, JsonTypeInfo<T> info, Cancel cancel)
    {
        var response = await client.Send(message, cancel);
        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Body);
        }

        var read = response.Read(info);
        if (read == null)
        {
            throw new InvalidOperationException("Empty response");
        }

        return read;
    }

    async Task<string> Send(Message message, Cancel cancel)
    {
        var response = await client.Send(message, cancel);
        var body = response.Body;

        if (!response.Ok)
        {
            throw new InvalidOperationException(body);
        }

        if (body.Length == 0)
        {
            return "Done";
        }

        return body;
    }
}
