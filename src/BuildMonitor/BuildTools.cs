using ModelContextProtocol.Server;

/// <summary>
/// The MCP tools. Thin: every one hands off to <see cref="MonitorTools"/>, which is what the
/// tests exercise; this class only carries the attributes the SDK reads.
/// </summary>
[McpServerToolType]
sealed class BuildTools(MonitorTools tools)
{
    [McpServerTool(Name = "list_builds", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the latest build of every monitored pipeline across every CI connection, on its default branch where the service says which that is, with status and timing. A pipeline's other branches, pull requests and pushed branches, whose latest run is running, queued or failed are listed too, each marked otherBranch. Passing builds are left out and counted in passingLeftOut, unless a filter is given or includePassing is set. Pass a filter to keep only builds whose pipeline, repository, branch or connection name contains it, passing ones included. Each build's links, commit, author, local checkout and whether it can be retried or cancelled are in get_build, which takes its key. Failures the user deferred are left out, as the tray leaves them out, so a pipeline missing here may be one they put off: pass includeDeferred to list them too, each carrying deferredUntil, or use list_deferred.")]
    public Task<BuildListDto> ListBuilds(
        [Description("Optional substring to filter on pipeline, repository, branch or connection name.")]
        string? filter = null,
        [Description("Also list the failures the user deferred, each marked with deferredUntil. Defaults to false.")]
        bool includeDeferred = false,
        [Description("Also list the passing builds when no filter is given. Every pipeline's build, a long list on a large account. Defaults to false.")]
        bool includePassing = false,
        Cancel cancel = default) =>
        tools.BuildList(filter, includeDeferred, includePassing, cancel);

    [McpServerTool(Name = "list_deferred", ReadOnly = true, UseStructuredContent = true)]
    [Description("The failed builds the user chose to put off for a day or more, which list_builds, list_failing and the summary's failing count leave out until the deferral ends or the pipeline next passes. Each carries deferredUntil. They are failures the user has already seen, so do not raise them as news.")]
    public Task<List<BuildDto>> ListDeferred(Cancel cancel = default) =>
        tools.ListDeferred(null, cancel);

    [McpServerTool(Name = "list_failing", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists only the pipelines whose own latest build failed, the one on their default branch. A pull request or other branch failing is not its pipeline failing: those are in list_builds, marked otherBranch. Failures the user deferred are not here: see list_deferred.")]
    public Task<List<BuildDto>> ListFailing(Cancel cancel = default) =>
        tools.ListFailing(null, cancel);

    [McpServerTool(Name = "get_build", ReadOnly = true, UseStructuredContent = true)]
    [Description("One build by its key, as returned by list_builds or list_deferred: status text, commit, author, links, the local checkout where there is one, whether it can be retried or cancelled, and deferredUntil where the user put its failure off.")]
    public Task<BuildDto> GetBuild(
        [Description("The build key from list_builds.")]
        string key,
        Cancel cancel = default) =>
        tools.GetBuild(key, cancel);

    [McpServerTool(Name = "list_runs", ReadOnly = true, UseStructuredContent = true)]
    [Description("The recent runs of one pipeline, newest first, where list_builds shows only the latest: this is what says whether a failure is the first or the fourth in a row. Covers every branch the pipeline ran on, as far back as the tray has polled. Runs on one branch share the build key and are told apart by their run number.")]
    public Task<List<BuildDto>> ListRuns(
        [Description("A build key from list_builds, or a pipeline key from list_pipelines.")]
        string key,
        Cancel cancel = default) =>
        tools.ListRuns(key, cancel);

    [McpServerTool(Name = "list_pipelines", ReadOnly = true, UseStructuredContent = true)]
    [Description("Every pipeline being monitored, including ones that have run nothing lately and so appear in no build list. Each carries a key that list_runs takes, and a count of the runs held for it.")]
    public Task<List<PipelineDto>> ListPipelines(Cancel cancel = default) =>
        tools.ListPipelines(cancel);

    [McpServerTool(Name = "get_build_log", ReadOnly = true)]
    [Description("The log of a build, fetched from the CI service: the logs of the jobs, steps or tasks that failed, each under a line naming it, or the whole build's log where the service keeps one log a build. Only the end of each is returned, under a count of the lines dropped before it. Fails when the build has no log, as a run that failed before starting a job has none.")]
    public Task<string> GetBuildLog(
        [Description("The build key from list_builds.")]
        string key,
        [Description("How many lines to keep from the end of each section. Defaults to 200.")]
        int maxLines = LogTail.DefaultLines,
        Cancel cancel = default) =>
        tools.GetLog(key, maxLines, cancel);

    [McpServerTool(Name = "download_build_artifacts", Idempotent = true, UseStructuredContent = true)]
    [Description("Downloads a failed build's artifacts to a local directory and writes its whole log beside them as log.txt, then returns that directory and what is in it. Read the files from disk with your own file tools: no content comes back through this call, and log.txt is the entire log where get_build_log returns only the end of each section. Artifacts too large to copy are named but not downloaded, and a service whose artifacts BuildMonitor cannot list says so rather than reporting that the build published none. The files are deleted after 24 hours. Can take a while on a large artifact.")]
    public Task<TriageFilesDto> DownloadBuildArtifacts(
        [Description("The build key from list_builds. The build must have failed.")]
        string key,
        Cancel cancel = default) =>
        tools.DownloadArtifacts(key, cancel);

    [McpServerTool(Name = "wait_for_build", ReadOnly = true, UseStructuredContent = true)]
    [Description("Waits for a build to finish, then returns it as get_build does, so you can tell the user when it is done rather than checking back. Returns at once when it has already finished. Follows the run it started on: a newer run on the same branch starting meanwhile is not the one waited on. Right after a push or retry_build pass nextRun, since the tray shows the last run until it polls the new one. Pass wholePush to wait for every pipeline the push started, not only this one, and stopOnFailure as well to hear of the first failure without waiting for the rest. When the timeout passes first, timedOut is true and the build is as it stood: call again to keep waiting. Asks the tray every few seconds, which costs no calls to the CI service.")]
    public Task<BuildWaitDto> WaitForBuild(
        [Description("The build key from list_builds.")]
        string key,
        [Description("When the run the key shows has already finished, wait for the one after it instead of returning it: the run a push or retry_build just set off. A run still going is waited on either way. The tray is asked to poll that pipeline now, so a pipeline that has been quiet needs no refresh first. Defaults to false.")]
        bool nextRun = false,
        [Description("Wait for every run of the push this run was part of, each pipeline of its repository that ran the same commit on the same branch, rather than this run alone, and list them all in pushRuns: what \"did my push pass?\" asks about. Fails for a build whose service reports no commit. Defaults to false.")]
        bool wholePush = false,
        [Description("With wholePush, return as soon as any run of the push fails, with the rest as they stood, rather than once they have all finished: you can start on the failure while the others run. A cancelled run is not a failure. Defaults to false; means nothing without wholePush, since a failure ends a wait on one run anyway.")]
        bool stopOnFailure = false,
        [Description("How long to wait, in seconds, before returning with timedOut. Defaults to 600, at most 3600. Pass less where your client gives up on a tool call sooner.")]
        int timeoutSeconds = 600,
        IProgress<ProgressNotificationValue>? progress = null,
        Cancel cancel = default) =>
        tools.WaitForBuild(
            key,
            nextRun,
            Scope(wholePush, stopOnFailure),
            TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 0, 3600)),
            // Seconds waited, which only grow, as a progress value must. The build's own progress
            // is an estimate that can stall or step back.
            (_, message) => progress?.Report(
                new()
                {
                    Progress = (float) _.TotalSeconds,
                    Message = message
                }),
            cancel);

    static WaitScope Scope(bool wholePush, bool stopOnFailure)
    {
        if (!wholePush)
        {
            return WaitScope.Run;
        }

        if (stopOnFailure)
        {
            return WaitScope.PushUntilFailure;
        }

        return WaitScope.Push;
    }

    [McpServerTool(Name = "summary", ReadOnly = true, UseStructuredContent = true)]
    [Description("Counts of pipelines, of those whose own build on their default branch is failing, and of running builds on any branch, the tray icon state, and the health of every connection. Deferred counts the failures the user put off, which the failing count leaves out.")]
    public Task<SummaryDto> Summary(Cancel cancel = default) =>
        tools.Summary(cancel);

    [McpServerTool(Name = "list_connections", ReadOnly = true, UseStructuredContent = true)]
    [Description("The configured CI connections and whether polling them works. Never returns credentials.")]
    public Task<List<ConnectionDto>> ListConnections(Cancel cancel = default) =>
        tools.ListConnections(cancel);

    [McpServerTool(Name = "refresh", Idempotent = true)]
    [Description("Polls the CI services now rather than at the next interval: one build's pipeline, one connection, or every connection. Returns as soon as the poll is asked for, before it has run, so list the builds again after a few seconds to see what it found. After a push, pass the key of a build of the pipeline it ran: only that pipeline is fetched, now and often for three minutes, where a connection is every pipeline it has. A running build is already polled often near when it should finish, so this is not needed to wait for one: wait_for_build does that.")]
    public Task<string> Refresh(
        [Description("A connection id from list_connections, or empty for every connection. Leave empty when passing key.")]
        string? connectionId = null,
        [Description("A build key from list_builds, to poll only that build's pipeline rather than a whole connection.")]
        string? key = null,
        Cancel cancel = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return tools.Refresh(string.IsNullOrWhiteSpace(connectionId) ? null : connectionId, cancel);
        }

        // Refused rather than one of them picked: whichever was dropped, the poll would not be the
        // one the assistant thought it asked for.
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            throw new McpException("Pass connectionId or key, not both.");
        }

        return tools.RefreshBuild(key, cancel);
    }

    [McpServerTool(Name = "retry_build")]
    [Description("Re-runs a finished build. Where the provider supports it only the failed jobs are re-run.")]
    public Task<string> RetryBuild(
        [Description("The build key from list_builds.")]
        string key,
        Cancel cancel = default) =>
        tools.RetryBuild(key, cancel);

    [McpServerTool(Name = "cancel_build")]
    [Description("Cancels a queued or running build.")]
    public Task<string> CancelBuild(
        [Description("The build key from list_builds.")]
        string key,
        Cancel cancel = default) =>
        tools.CancelBuild(key, cancel);

    [McpServerTool(Name = "run_build_next")]
    [Description("Moves a build that is still queued to the front of its service's queue, so it is the next one to start. Only some services can reorder a queue; a build that says canRunNext is one of them.")]
    public Task<string> RunBuildNext(
        [Description("The build key from list_builds.")]
        string key,
        Cancel cancel = default) =>
        tools.RunBuildNext(key, cancel);

    [McpServerTool(Name = "open_build_in_browser")]
    [Description("Opens a build's page, its branch, or its pull request in the user's browser.")]
    public Task<string> OpenBuild(
        [Description("The build key from list_builds.")]
        string key,
        [Description("Which link to open: build, branch or pr.")]
        string which = "build",
        Cancel cancel = default) =>
        tools.OpenBuild(key, which, cancel);
}
