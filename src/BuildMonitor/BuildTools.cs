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
        tools.ListFailing(cancel);

    [McpServerTool(Name = "get_build", ReadOnly = true, UseStructuredContent = true)]
    [Description("One build by its key, as returned by list_builds: status text, commit, author, links, whether it can be retried or cancelled.")]
    public Task<BuildDto> GetBuild(
        [Description("The build key from list_builds.")] string key,
        Cancel cancel = default) =>
        tools.GetBuild(key, cancel);

    [McpServerTool(Name = "summary", ReadOnly = true, UseStructuredContent = true)]
    [Description("Counts of failing and running builds, the tray icon state, and the health of every connection.")]
    public Task<SummaryDto> Summary(Cancel cancel = default) =>
        tools.Summary(cancel);

    [McpServerTool(Name = "list_connections", ReadOnly = true, UseStructuredContent = true)]
    [Description("The configured CI connections and whether polling them works. Never returns credentials.")]
    public Task<List<ConnectionDto>> ListConnections(Cancel cancel = default) =>
        tools.ListConnections(cancel);

    [McpServerTool(Name = "refresh", Idempotent = true)]
    [Description("Polls the CI services now rather than waiting for the next interval.")]
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
