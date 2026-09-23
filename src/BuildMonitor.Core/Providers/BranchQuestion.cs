/// <summary>
/// What the service holding a repository is asked about a failed branch: the repository's address,
/// the branch as its row names it, a fork's behind the fork's owner, and the pull request its run was
/// for, where it names one.
/// </summary>
record BranchQuestion(string Repository, string Branch, string? PullRequest)
{
    public string Key => BranchVerdicts.KeyOf(Repository, Branch, PullRequest);
}
