/// <summary>
/// One of the user's open pull requests to a repository discovery does not list: the branch its
/// runs are on, and the repository that branch is in, which tells them from a branch of the same
/// name in the repository itself or in someone else's fork.
/// </summary>
record GitHubOpenPullRequest(long Number, string Branch, string HeadRepository);
