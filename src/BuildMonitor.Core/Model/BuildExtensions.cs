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
    /// The last segment only: the owner is the same for most of a connection's rows and the full
    /// name is in the tooltip.
    /// </summary>
    public static string ShortRepoName(this Build build) =>
        ShortRepoName(build.RepoName);

    public static string ShortRepoName(string repoName) =>
        repoName[(repoName.LastIndexOf('/') + 1)..];
}
