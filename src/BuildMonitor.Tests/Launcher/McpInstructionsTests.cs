using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

/// <summary>
/// What an assistant is told when it connects. It names tools and a prompt, so a rename would
/// leave it sending the assistant after something the server no longer has.
/// </summary>
public class McpInstructionsTests
{
    [Test]
    public Task WhatItSays() =>
        Verify(McpInstructions.Text)
            .Snapshot(
                """
                BuildMonitor watches the user's CI builds and deployments on AppVeyor, Azure DevOps, Bitbucket Pipelines, GitHub Actions, GitLab CI, GoCD, Jenkins, Octopus Deploy, TeamCity and Travis CI, across every connection the user has set up. Questions about builds, pipelines, CI runs, failures or deployments usually mean these, even when BuildMonitor is not named: "what's building?", "is main green?", "why did the release fail?".

                Start from list_builds, or list_failing for failures. Each build they return has a key, which get_build, list_runs, get_build_log, retry_build, cancel_build and open_build_in_browser take. A build whose repository is checked out on this machine also has a directory, the path of that checkout. Read a failure's log with get_build_log before deciding what caused it. Where the log points at a file the run published, such as a test report or a crash dump, download_build_artifacts writes that build's artifacts and its whole log to a local directory for you to read from disk. When several builds fail, compare their logs before investigating each: one shared workflow or dependency often breaks many.

                The builds are as of the tray's last poll. A running build is already polled often near when it should finish, so to wait for one, list it again later rather than refreshing. A pipeline that has been quiet is polled less often, so after a push call refresh, then list again after a few seconds: refresh returns before the poll has run. Missing or stale builds for a service usually mean its connection is failing, and list_connections says why. Only the user can sign in again, from the tray.

                Use retry_build and cancel_build only when asked, not to test a theory: they change the user's CI. To work through several failures at once, the user can run the triage prompt.
                """);

    [Test]
    public async Task EveryToolItNamesIsOneTheServerHas()
    {
        var tools = typeof(BuildTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(_ => _.GetCustomAttribute<McpServerToolAttribute>()!.Name)
            .ToList();
        var named = Regex.Matches(McpInstructions.Text, @"\b[a-z]+(?:_[a-z]+)+\b")
            .Select(_ => _.Value)
            .Distinct()
            .ToList();

        await Assert.That(named).IsNotEmpty();
        await Assert.That(named.Except(tools)).IsEmpty();
    }

    [Test]
    public async Task ThePromptItNamesIsOneTheServerHas()
    {
        var prompts = typeof(BuildPrompts)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(_ => _.GetCustomAttribute<McpServerPromptAttribute>()!.Name);

        await Assert.That(McpInstructions.Text).Contains("the triage prompt");
        await Assert.That(prompts).Contains("triage");
    }
}
