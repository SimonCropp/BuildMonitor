/// <summary>
/// What the server tells an assistant when it connects, before it has read a single tool. A client
/// that defers its tools shows only their names until one is needed, so without this a question
/// that never says BuildMonitor, such as "what's building?", is matched against a list of names,
/// and inside a repository it reads just as well as a question about a local build.
/// <para>
/// It goes to every session, so it holds only what the tool descriptions cannot say for
/// themselves: what the builds are, and how the tools lead into each other. The services are
/// listed from <see cref="ProviderDescriptors.All"/>, so one added there is named here too.
/// </para>
/// </summary>
static class McpInstructions
{
    public static string Text { get; } = Compose([.. ProviderDescriptors.All.Select(_ => _.Name)]);

    static string Compose(string[] services) =>
        $"""
        BuildMonitor watches the user's CI builds and deployments on {string.Join(", ", services[..^1])} and {services[^1]}, across every connection the user has set up. Questions about builds, pipelines, CI runs, failures or deployments usually mean these, even when BuildMonitor is not named: "what's building?", "is main green?", "why did the release fail?".

        Start from list_builds, or list_failing for failures. Each build they return has a key, which get_build, list_runs, get_build_log, retry_build, cancel_build and open_build_in_browser take. A build whose repository is checked out on this machine also has a directory, the path of that checkout. Read a failure's log with get_build_log before deciding what caused it. When several builds fail, compare their logs before investigating each: one shared workflow or dependency often breaks many.

        The builds are as of the tray's last poll. A running build is already polled often near when it should finish, so to wait for one, list it again later rather than refreshing. A pipeline that has been quiet is polled less often, so after a push call refresh, then list again after a few seconds: refresh returns before the poll has run. Missing or stale builds for a service usually mean its connection is failing, and list_connections says why. Only the user can sign in again, from the tray.

        Use retry_build and cancel_build only when asked, not to test a theory: they change the user's CI. To work through several failures at once, the user can run the triage prompt.
        """;
}
