/// <summary>
/// Why another branch's newest run has no row, which the hover of its pipeline's row says beside it.
/// </summary>
public enum FoldReason
{
    // It passed, or was cancelled: history rather than something to watch or to read.
    Settled,
    // It failed, and its pull request has been merged since.
    Merged,
    // It failed, and its pull request has been closed since.
    Closed,
    // It failed, and its branch has been deleted since.
    Deleted,
    // It failed, nothing could say whether its branch is still there, and the pipeline's default
    // branch has built since.
    Superseded,
    // It failed, the pipeline's default branch has built since, and the service holding its
    // repository has yet to say whether its branch is still there. Most such branches are a Dependabot update closed long since, and every one of them
    // otherwise flashed red at start-up until the answers came.
    Asking
}
