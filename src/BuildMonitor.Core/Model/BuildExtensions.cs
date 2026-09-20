static class BuildExtensions
{
    /// <summary>
    /// Whether the run, rather than the project, is what the row is about: one that broke, and one
    /// still going, which is the next thing to break or to go green. Such a row leads with its run;
    /// a settled green one leads with the project, since its run is of no interest.
    /// <para>
    /// A cancelled or unknown build is not one of them. Nobody is waiting on it and nothing broke,
    /// so it reads as the project's row like a passing one.
    /// </para>
    /// </summary>
    public static bool NeedsAttention(this Build build) =>
        build.Status is BuildStatus.Failed or BuildStatus.Running or BuildStatus.Queued;

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
    /// Whether this run can be moved to the front of its queue: the service has a call for it, the
    /// run is still queued, and the credential may change it.
    /// <para>
    /// Change is read off <see cref="Build.CanCancel"/> rather than a flag of its own. Cancelling a
    /// queued build and jumping it up the queue are the same right on both services that offer
    /// either, so a connection that may only watch loses this button with the other two, through
    /// the one <see cref="WatchOnly"/> call.
    /// </para>
    /// </summary>
    public static bool CanRunNext(this Build build, ProviderDescriptor descriptor) =>
        descriptor.HasQueuePriority &&
        build is
        {
            Status: BuildStatus.Queued,
            CanCancel: true
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
    /// Whether it is worth asking the service for this build's files: the service has an artifact
    /// API, and the build failed, for the same reason a log is only offered for a failure.
    /// <para>
    /// Says nothing about whether the build actually uploaded anything. Finding that out costs a
    /// request, and a chip that appears a second after its row does is worse than one that turns
    /// out to report an empty build.
    /// </para>
    /// </summary>
    public static bool ArtifactsListable(this Build build, ProviderDescriptor descriptor) =>
        descriptor.HasArtifacts &&
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
    /// For a caller that only compares the name. The string overload copies it out of the full
    /// name, which grouping and searching did for every build on every projection of the rows.
    /// </summary>
    public static CharSpan ShortRepoName(CharSpan repoName) =>
        repoName[(repoName.LastIndexOf('/') + 1)..];

    /// <summary>
    /// The branch as a row, a failure notification and the status verb name it, empty for a build with
    /// none. See <see cref="DependabotBranches"/>.
    /// </summary>
    public static string ShortBranchName(this Build build) =>
        ShortBranchName(build.Branch);

    public static string ShortBranchName(string? branch) =>
        DependabotBranches.Short(branch ?? "");
}
