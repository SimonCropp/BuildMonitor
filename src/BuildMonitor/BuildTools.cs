using ModelContextProtocol.Server;

/// <summary>
/// The MCP tools. Thin: every one hands off to <see cref="MonitorTools"/>, which is what the
/// tests exercise; this class only carries the attributes the SDK reads.
/// </summary>
[McpServerToolType]
sealed class BuildTools(MonitorTools tools)
{
    [McpServerTool(Name = "list_builds", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists the latest build of every monitored pipeline across every CI connection, with status, timing, progress and links. Pass a filter to keep only builds whose pipeline, repository, branch or connection name contains it.")]
    public Task<List<BuildDto>> ListBuilds(
        [Description("Optional substring to filter on pipeline, repository, branch or connection name.")] string? filter = null,
        Cancel cancel = default) =>
        tools.ListBuilds(filter, cancel);

    [McpServerTool(Name = "list_failing", ReadOnly = true, UseStructuredContent = true)]
    [Description("Lists only the pipelines whose latest build failed.")]
    public Task<List<BuildDto>> ListFailing(Cancel cancel = default) =>
        tools.ListFailing(null, cancel);

    [McpServerTool(Name = "get_build", ReadOnly = true, UseStructuredContent = true)]
    [Description("One build by its key, as returned by list_builds: status text, commit, author, links, whether it can be retried or cancelled.")]
    public Task<BuildDto> GetBuild(
        [Description("The build key from list_builds.")] string key,
        Cancel cancel = default) =>
        tools.GetBuild(key, cancel);

    [McpServerTool(Name = "list_runs", ReadOnly = true, UseStructuredContent = true)]
    [Description("The recent runs of one pipeline, newest first, where list_builds shows only the latest: this is what says whether a failure is the first or the fourth in a row. Covers every branch the pipeline ran on, as far back as the tray has polled. Runs on one branch share the build key and are told apart by their run number.")]
    public Task<List<BuildDto>> ListRuns(
        [Description("A build key from list_builds, or a pipeline key from list_pipelines.")] string key,
        Cancel cancel = default) =>
        tools.ListRuns(key, cancel);

    [McpServerTool(Name = "list_pipelines", ReadOnly = true, UseStructuredContent = true)]
    [Description("Every pipeline being monitored, including ones that have run nothing lately and so appear in no build list. Each carries a key that list_runs takes, and a count of the runs held for it.")]
    public Task<List<PipelineDto>> ListPipelines(Cancel cancel = default) =>
        tools.ListPipelines(cancel);

    [McpServerTool(Name = "get_build_log", ReadOnly = true)]
    [Description("The log of a build, fetched from the CI service: the logs of the jobs, steps or tasks that failed, each under a line naming it, or the whole build's log where the service keeps one log a build. Only the end of each is returned, under a count of the lines dropped before it. Fails when the build has no log, as a run that failed before starting a job has none.")]
    public Task<string> GetBuildLog(
        [Description("The build key from list_builds.")] string key,
        [Description("How many lines to keep from the end of each section. Defaults to 200.")] int maxLines = LogTail.DefaultLines,
        Cancel cancel = default) =>
        tools.GetLog(key, maxLines, cancel);

    [McpServerTool(Name = "summary", ReadOnly = true, UseStructuredContent = true)]
    [Description("Counts of failing and running builds, the tray icon state, and the health of every connection.")]
    public Task<SummaryDto> Summary(Cancel cancel = default) =>
        tools.Summary(cancel);

    [McpServerTool(Name = "list_connections", ReadOnly = true, UseStructuredContent = true)]
    [Description("The configured CI connections and whether polling them works. Never returns credentials.")]
    public Task<List<ConnectionDto>> ListConnections(Cancel cancel = default) =>
        tools.ListConnections(cancel);

    [McpServerTool(Name = "refresh", Idempotent = true)]
    [Description("Polls the CI services now rather than at the next interval. Returns as soon as the poll is asked for, before it has run, so list the builds again after a few seconds to see what it found. A running build is already polled often near when it should finish, so this is not needed to wait for one.")]
    public Task<string> Refresh(
        [Description("A connection id from list_connections, or empty for every connection.")] string? connectionId = null,
        Cancel cancel = default) =>
        tools.Refresh(string.IsNullOrWhiteSpace(connectionId) ? null : connectionId, cancel);

    [McpServerTool(Name = "retry_build")]
    [Description("Re-runs a finished build. Where the provider supports it only the failed jobs are re-run.")]
    public Task<string> RetryBuild(
        [Description("The build key from list_builds.")] string key,
        Cancel cancel = default) =>
        tools.RetryBuild(key, cancel);

    [McpServerTool(Name = "cancel_build")]
    [Description("Cancels a queued or running build.")]
    public Task<string> CancelBuild(
        [Description("The build key from list_builds.")] string key,
        Cancel cancel = default) =>
        tools.CancelBuild(key, cancel);

    [McpServerTool(Name = "open_build_in_browser")]
    [Description("Opens a build's page, its branch, or its pull request in the user's browser.")]
    public Task<string> OpenBuild(
        [Description("The build key from list_builds.")] string key,
        [Description("Which link to open: build, branch or pr.")] string which = "build",
        Cancel cancel = default) =>
        tools.OpenBuild(key, which, cancel);
}
