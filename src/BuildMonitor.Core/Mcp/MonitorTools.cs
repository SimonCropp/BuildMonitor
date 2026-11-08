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
        var failed = await Failing(cancel);
        if (string.IsNullOrWhiteSpace(filter))
        {
            return failed;
        }

        return failed
            .Where(_ => Matches(_, filter))
            .ToList();
    }

    /// <summary>
    /// The failing builds alone, picked out by the tray. A tray older than the verb answers it as
    /// unreadable, and is asked for every build instead, the failing ones picked out here, as they
    /// all were before the tray could.
    /// </summary>
    async Task<List<BuildDto>> Failing(Cancel cancel)
    {
        var response = await client.Send(new(Verb.Failing), cancel);
        if (response.Ok)
        {
            return response.Read(DtoContext.Default.ListBuildDto) ??
                   throw new InvalidOperationException("Empty response");
        }

        if (response.Body != LocalServer.Unreadable)
        {
            throw new InvalidOperationException(response.Body);
        }

        var builds = await Read(new(Verb.List), DtoContext.Default.ListBuildDto, cancel);
        return builds
            .Where(_ => _.Status == nameof(BuildStatus.Failed) &&
                        _.OtherBranch != true)
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
    /// and an assistant handed that would report the push's result before it had run. The tray is
    /// asked to poll that pipeline now and often for a few minutes, as a retry from the window
    /// does: a pipeline that had been quiet is otherwise polled rarely, and the wait sat until it
    /// was, unless the assistant remembered a refresh, which fetches every pipeline of the
    /// connection for this one.
    /// </para>
    /// <para>
    /// With a <paramref name="scope"/> of a push, the wait is for every run of the push that run
    /// was part of, across the repository's pipelines: see <see cref="WaitForPush"/>.
    /// </para>
    /// </summary>
    /// <param name="waiting">Told how long the wait has run and what it is waiting on, before each pause.</param>
    public async Task<BuildWaitDto> WaitForBuild(string key, bool nextRun, WaitScope scope, TimeSpan timeout, Action<TimeSpan, string>? waiting, Cancel cancel)
    {
        var watch = Stopwatch.StartNew();
        var build = await Latest(key, cancel);
        if (nextRun &&
            !IsActive(build))
        {
            // The answer goes unread. A tray older than the verb refuses it as unreadable, and
            // the wait still works, only as slowly as it did before there was a nudge.
            await client.Send(new(Verb.Nudge, key), cancel);
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

        if (scope != WaitScope.Run)
        {
            return await WaitForPush(key, build, scope == WaitScope.PushUntilFailure, watch, timeout, waiting, cancel);
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

    /// <summary>
    /// Waits until every run of the push <paramref name="build"/> was part of has finished: "did my
    /// push pass?" is about every pipeline it started, and a wait on one of them answered for that
    /// one alone, a docs build passing while the tests still ran.
    /// <para>
    /// The runs are asked for again each time rather than fixed when the wait began. A push's
    /// pipelines are not all polled at once, so one can show up after another has finished, and a
    /// wait that had counted only the first would end before it.
    /// </para>
    /// <para>
    /// With <paramref name="untilFailure"/> it ends at the first failure too, with the rest as they
    /// stood, so an assistant can start on the failure while the other runs go on. A push that had
    /// failed already when the wait began is answered at once, as a finished run is.
    /// </para>
    /// </summary>
    async Task<BuildWaitDto> WaitForPush(string key, BuildDto build, bool untilFailure, Stopwatch watch, TimeSpan timeout, Action<TimeSpan, string>? waiting, Cancel cancel)
    {
        var run = build.Run;
        var runs = await PushRuns(key, run, cancel);
        while (runs.Any(IsActive) &&
               !(untilFailure && runs.Any(IsFailed)))
        {
            var finished = runs.Count(_ => !IsActive(_));
            if (!await Pause($"{finished} of {runs.Count} runs of {build.Repo} at {Short(build.Commit)} finished", watch, timeout, waiting, cancel))
            {
                return Pushed(true, key, run, runs);
            }

            runs = await PushRuns(key, run, cancel);
        }

        return Pushed(false, key, run, runs);
    }

    static BuildWaitDto Pushed(bool timedOut, string key, string run, List<BuildDto> runs) =>
        new(timedOut, runs.First(_ => _.Key == key && _.Run == run), [.. runs.Select(Row)]);

    static string? Short(string? commit)
    {
        if (commit is { Length: > 7 })
        {
            return commit[..7];
        }

        return commit;
    }

    /// <summary>
    /// The runs of the push one run was part of.
    /// </summary>
    async Task<List<BuildDto>> PushRuns(string key, string run, Cancel cancel)
    {
        var response = await client.Send(new(Verb.PushRuns, key, run), cancel);
        if (response.Ok)
        {
            return response.Read(DtoContext.Default.ListBuildDto) ??
                   throw new InvalidOperationException("Empty response");
        }

        throw Refused(response, "list the runs of a push: quit the tray from its menu and start BuildMonitor again");
    }

    /// <summary>
    /// What a refused verb is reported as. A tray older than the verb answers it as unreadable,
    /// which is told apart from a refusal, since only the user can do anything about it, and only
    /// once told what.
    /// </summary>
    /// <param name="cannot">What the older tray cannot do, and what to do instead.</param>
    static InvalidOperationException Refused(Response response, string cannot)
    {
        if (response.Body == LocalServer.Unreadable)
        {
            return new($"The tray running is older than this server and cannot {cannot}.");
        }

        return new(response.Body);
    }

    static bool IsActive(BuildDto build) =>
        build.Status is nameof(BuildStatus.Queued) or nameof(BuildStatus.Running);

    /// <summary>
    /// Failed, and not cancelled: a run cancelled for a newer push, or by hand, says nothing about
    /// whether this one passes.
    /// </summary>
    static bool IsFailed(BuildDto build) =>
        build.Status == nameof(BuildStatus.Failed);

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
    /// The run a wait on the key would start from, as <see cref="Latest"/> finds it.
    /// </summary>
    public Task<BuildDto> Current(string key, Cancel cancel) =>
        Latest(key, cancel);

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

        // Asked of the runs directly rather than through ListRuns, whose refusal names a pipeline:
        // passed on, a mistyped build key was answered "No pipeline with key".
        var response = await client.Send(new(Verb.Runs, key), cancel);
        if (response.Ok &&
            response.Read(DtoContext.Default.ListBuildDto)?.FirstOrDefault(_ => _.Key == key) is { } latest)
        {
            return latest;
        }

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

    /// <summary>
    /// Polls one build's pipeline now and at the poll interval for three minutes, as a retry from
    /// the window does. After a push only that pipeline has news, and a refresh of its connection
    /// fetched every pipeline the connection has for it, hundreds on a large account.
    /// </summary>
    public async Task<string> RefreshBuild(string key, Cancel cancel)
    {
        var response = await client.Send(new(Verb.Nudge, key), cancel);
        if (!response.Ok)
        {
            throw Refused(response, "refresh one build: refresh its connection instead, or quit the tray from its menu and start BuildMonitor again");
        }

        return $"Refreshing {key}";
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
