/// <summary>
/// What the service holding a repository is asked about a failed branch: the repository's address,
/// the branch as its row names it, a fork's behind the fork's owner, the pull request its run was
/// for, where it names one, and the commit the run built. The commit is not part of the key: it
/// only lets a service match a pull request the run did not name to the run.
/// </summary>
record BranchQuestion(string Repository, string Branch, string? PullRequest, string? Commit = null)
{
    public string Key => BranchVerdicts.KeyOf(Repository, Branch, PullRequest);
}
