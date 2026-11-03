class GitHubPullRequestHead
{
    public string? Sha { get; set; }
    // The branch, in the repository it lives in: a fork for a pull request from one.
    public string? Ref { get; set; }
    // Null once that fork is deleted.
    public GitHubRepository? Repo { get; set; }
}
