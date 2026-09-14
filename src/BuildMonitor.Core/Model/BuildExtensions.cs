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
    /// The last segment only: the owner is the same for most rows, and a column of repeated
    /// "VerifyTests/" prefixes pushes the part that differs out of view.
    /// </summary>
    public static string ShortRepoName(this Build build) =>
        ShortRepoName(build.RepoName);

    public static string ShortRepoName(string repoName) =>
        repoName[(repoName.LastIndexOf('/') + 1)..];
}
