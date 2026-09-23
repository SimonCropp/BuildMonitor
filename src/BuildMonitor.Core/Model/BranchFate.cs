/// <summary>
/// What became of another branch whose run failed, as the service holding its repository says. A
/// failed branch keeps a row while it can still be fixed, and a merged or closed pull request, or a
/// deleted branch, cannot be: a Dependabot update closed in favour of a newer one sat red on the
/// screen for as long as its run stayed in the history.
/// </summary>
public enum BranchFate
{
    // Its pull request is open, or its branch, or a tag of that name, is still there.
    Open,
    // Its pull request was merged.
    Merged,
    // Its pull request was closed without being merged.
    Closed,
    // Its branch is gone from the repository.
    Deleted,
    // The service could not say: the credential cannot see the repository or its pull requests, or
    // the branch is a fork's that no pull request names.
    Unknown
}
