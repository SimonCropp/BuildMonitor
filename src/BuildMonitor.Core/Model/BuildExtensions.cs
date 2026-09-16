static class BuildExtensions
{
    public static string RunNumberLabel(this Build build)
    {
        if (build.RunNumber.Length == 0)
        {
            return "";
        }

        return $"#{build.RunNumber}";
    }

    /// <summary>
    /// Offered only for a build that did not pass. GitHub will re-run a green run, but a Retry chip
    /// on every green row asks for something nobody wants and buries the ones that matter.
    /// </summary>
    public static bool Retryable(this Build build) =>
        build is
        {
            CanRetry: true,
            Status: BuildStatus.Failed or BuildStatus.Cancelled
        };

    /// <summary>
    /// The build as a connection that may only watch it has it: no retry and no cancel, whatever its
    /// state would allow. Every surface that offers either reads the flags, so clearing them here
    /// takes both off the chips, the menus, the launcher and the MCP tools at once.
    /// </summary>
    public static Build WatchOnly(this Build build)
    {
        if (build is { CanRetry: false, CanCancel: false })
        {
            return build;
        }

        return build with { CanRetry = false, CanCancel = false };
    }

    /// <summary>
    /// Offered only for a failed build. A passing or cancelled run's log answers no question and a
    /// running one's is still being written, so a chip on every row would bury the logs that matter
    /// the way a Retry chip on green rows would.
    /// </summary>
    public static bool LogCopyable(this Build build) =>
        build.Status == BuildStatus.Failed;

    /// <summary>
    /// The last segment only: the owner is the same for most rows, and a column of repeated
    /// "VerifyTests/" prefixes pushes the part that differs out of view.
    /// </summary>
    public static string ShortRepoName(this Build build) =>
        ShortRepoName(build.RepoName);

    public static string ShortRepoName(string repoName) =>
        repoName[(repoName.LastIndexOf('/') + 1)..];

    /// <summary>
    /// The branch as a row names it, empty for a build with none. See <see cref="DependabotBranches"/>.
    /// </summary>
    public static string ShortBranchName(this Build build) =>
        DependabotBranches.Short(build.Branch ?? "");
}
