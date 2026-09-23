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

        Start from list_builds, or list_failing for failures. A pipeline's build is its run on the default branch; its pull requests and other branches that are running, queued or failed are listed too, marked otherBranch, and one of those failing is not its pipeline failing, so list_failing leaves them out. Each build they return has a key, which get_build, list_runs, get_build_log, retry_build, cancel_build, run_build_next and open_build_in_browser take. A build whose repository is checked out on this machine also has a directory, the path of that checkout. Read a failure's log with get_build_log before deciding what caused it. Where the log points at a file the run published, such as a test report or a crash dump, download_build_artifacts writes that build's artifacts and its whole log to a local directory for you to read from disk. When several builds fail, compare their logs before investigating each: one shared workflow or dependency often breaks many.

        The builds are as of the tray's last poll. A running build is already polled often near when it should finish, so to wait for one, list it again later rather than refreshing. A pipeline that has been quiet is polled less often, so after a push call refresh, then list again after a few seconds: refresh returns before the poll has run. Missing or stale builds for a service usually mean its connection is failing, and list_connections says why. Only the user can sign in again, from the tray.

        Use retry_build, cancel_build and run_build_next only when asked, not to test a theory: they change the user's CI. run_build_next only suits a build still waiting on a service that can reorder its queue, which the build's canRunNext says. To work through several failures at once, the user can run the triage prompt.
        """;
}
